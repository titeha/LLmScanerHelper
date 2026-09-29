using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using LlmScanHelper.Models;

namespace LlmScanHelper.Tests
{
    /// <summary>
    /// Минимальный набор вспомогательных методов для создания детерминированных GGUF-файлов в тестах (S0).
    /// Размеры тензоров считаются по таблице S1 (<see cref="GgmlTypes.Bytes"/>), а не «на глаз».
    /// Запись — только во временный каталог (см. <see cref="TempDir"/>).
    /// </summary>
    internal static class GgufFixtures
    {
        /// <summary>Точная раскладка записанного файла (для проверок парсера).</summary>
        public readonly record struct FixtureLayout(long HeaderEnd, long DataStart, long FileLength, long TotalTensorBytes);

        /// <summary>Описание тензора для записи заголовка GGUF.</summary>
        public sealed class TensorInfo
        {
            public string Name { get; set; } = "";
            /// <summary>Размерности (до 4; меньше — дополняются единицами в формате не хранятся).</summary>
            public ulong[] Dims { get; set; } = Array.Empty<ulong>();
            public int TypeId { get; set; }
        }

        /// <summary>
        /// Записывает одиночный GGUF-файл.
        /// </summary>
        /// <param name="path">Полный путь к файлу.</param>
        /// <param name="kv">KV-пары: ключ → (typeId, value).</param>
        /// <param name="tensors">Список тензоров (может быть пустым).</param>
        /// <param name="alignment">Выравнивание data-section (по умолчанию 32, степень двойки).</param>
        /// <param name="version">Версия GGUF (по умолчанию 3).</param>
        /// <param name="tailPadding">Дополнительный нулевой padding после data-section.</param>
        /// <param name="lastTensorOffsetBump">Сдвиг offset последнего тензора (дефект «offset за файлом»).</param>
        /// <param name="writeData">Писать ли data section (false — только заголовок + tensor-info,
        /// для дефектов, где размеры тензоров непредставимо велики).</param>
        public static FixtureLayout Write(
            string path,
            IDictionary<string, (int typeId, object value)> kv,
            IList<TensorInfo> tensors,
            uint alignment = 32,
            uint version = 3,
            uint tailPadding = 0,
            long lastTensorOffsetBump = 0,
            bool writeData = true)
        {
            // nbytes по таблице S1; для типа вне таблицы размер не гарантируется (S0-контракт) → 0.
            var nbytes = new long[tensors.Count];
            for (int i = 0; i < tensors.Count; i++)
            {
                var t = tensors[i];
                if (!GgmlTypes.TryGet((uint)t.TypeId, out _)) continue;
                var dims = new long[4] { 1, 1, 1, 1 };
                for (int d = 0; d < t.Dims.Length && d < 4; d++) dims[d] = (long)t.Dims[d];
                nbytes[i] = GgmlTypes.Bytes(dims, (uint)t.TypeId);
            }

            // offset[i] = сумма GGML_PAD(nbytes, alignment) предыдущих тензоров.
            var offsets = new long[tensors.Count];
            long cur = 0;
            for (int i = 0; i < tensors.Count; i++)
            {
                offsets[i] = cur;
                cur += Pad(nbytes[i], alignment);
            }
            if (tensors.Count > 0 && lastTensorOffsetBump != 0)
                offsets[tensors.Count - 1] += lastTensorOffsetBump;

            // Данные пишутся по НЕсдвинутым offset'ам: сдвиг имитирует только порченный offset в tensor-info.
            long payloadEnd = 0;
            {
                long cur2 = 0;
                for (int i = 0; i < tensors.Count; i++)
                {
                    payloadEnd = Math.Max(payloadEnd, cur2 + nbytes[i]);
                    cur2 += Pad(nbytes[i], alignment);
                }
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var w = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false);

            // Header
            w.Write(0x46554747u); // "GGUF"
            w.Write(version);
            w.Write((ulong)tensors.Count);
            w.Write((ulong)kv.Count);

            // KV pairs: key, type u32, value
            foreach (var kvp in kv)
            {
                WriteString(w, kvp.Key);
                w.Write((uint)kvp.Value.typeId);
                WriteValue(w, kvp.Value.typeId, kvp.Value.value);
            }

            // Tensor-infos: name, n_dims u32, ne[n_dims] u64, type u32, offset u64 (спека: ровно n_dims значений).
            for (int i = 0; i < tensors.Count; i++)
            {
                var t = tensors[i];
                WriteString(w, t.Name);
                w.Write((uint)t.Dims.Length);
                foreach (var dim in t.Dims) w.Write(dim);
                w.Write((uint)t.TypeId);
                w.Write((ulong)offsets[i]);
            }

            long headerEnd = fs.Position;
            // data section выравнивается только если тензоров > 0 (gguf.cpp:773).
            long dataStart = tensors.Count > 0 ? Align(headerEnd, alignment) : headerEnd;
            WriteZeros(w, dataStart - headerEnd);

            if (writeData) WriteZeros(w, payloadEnd + tailPadding);

            return new FixtureLayout(headerEnd, dataStart, fs.Position, nbytes.Sum());
        }

        /// <summary>
        /// Записывает набор шардов `stem-0000N-of-0000M.gguf`. <paramref name="modelKv"/> (general.*,
        /// tokenizer.*) пишется только в шард 00001 (split.no == 0) — как у реальных наборов.
        /// Тензоры и KV-переопределения задаются по **номеру шарда из имени** (1-based).
        /// </summary>
        public static void WriteSplitSet(
            string dir,
            string stem,
            int shards,
            IDictionary<string, (int typeId, object value)> modelKv,
            IReadOnlyDictionary<int, IList<TensorInfo>>? tensorsByShard = null,
            IReadOnlyDictionary<int, IDictionary<string, (int typeId, object value)>>? perShardKv = null,
            int? splitTensorsCount = null,
            uint alignment = 32)
        {
            Directory.CreateDirectory(dir);
            int total = splitTensorsCount ?? (tensorsByShard?.Values.Sum(v => v.Count) ?? 0);

            for (int shardNo = 1; shardNo <= shards; shardNo++)
            {
                var kv = new Dictionary<string, (int, object)>();
                if (shardNo == 1)
                    foreach (var e in modelKv) kv[e.Key] = e.Value;

                kv["split.no"] = (2, (ushort)(shardNo - 1)); // 0-based в метаданных
                kv["split.count"] = (2, (ushort)shards);     // u16
                kv["split.tensors.count"] = (5, total);      // i32
                if (perShardKv != null && perShardKv.TryGetValue(shardNo, out var extra))
                    foreach (var e in extra) kv[e.Key] = e.Value;

                var tensors = tensorsByShard != null && tensorsByShard.TryGetValue(shardNo, out var tl)
                    ? tl
                    : new List<TensorInfo>();

                string fileName = $"{stem}-{shardNo:D5}-of-{shards:D5}.gguf";
                Write(Path.Combine(dir, fileName), kv, tensors, alignment: alignment);
            }
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            w.Write((ulong)bytes.Length);
            w.Write(bytes);
        }

        private static void WriteValue(BinaryWriter w, int typeId, object value)
        {
            // scalar types
            if (typeId >= 0 && typeId <= 7)
            {
                switch (typeId)
                {
                    case 0: w.Write(Convert.ToByte(value)); break; // uint8
                    case 1: w.Write(Convert.ToSByte(value)); break; // int8
                    case 2: w.Write(Convert.ToUInt16(value)); break;
                    case 3: w.Write(Convert.ToInt16(value)); break;
                    case 4: w.Write(Convert.ToUInt32(value)); break;
                    case 5: w.Write(Convert.ToInt32(value)); break;
                    case 6: w.Write(Convert.ToSingle(value)); break;
                    case 7: w.Write((bool)value ? (byte)1 : (byte)0); break;
                }
                return;
            }
            if (typeId == 8)
            {
                WriteString(w, value?.ToString() ?? string.Empty);
                return;
            }
            if (typeId == 9)
            {
                // Expect IEnumerable<object> where first element is element type id
                var arr = ((IEnumerable<object>)value).ToArray();
                int elemType = Convert.ToInt32(arr[0]);
                ulong count = (ulong)arr.Length - 1;
                w.Write((uint)elemType);
                w.Write(count);
                for (int idx = 1; idx < arr.Length; idx++)
                    WriteValue(w, elemType, arr[idx]);
                return;
            }
            if (typeId == 10) { w.Write(Convert.ToUInt64(value)); return; }
            if (typeId == 11) { w.Write(Convert.ToInt64(value)); return; }
            if (typeId == 12) { w.Write(Convert.ToDouble(value)); return; }

            throw new NotSupportedException($"Unsupported GGUF KV type id {typeId}");
        }

        private static void WriteZeros(BinaryWriter w, long count)
        {
            if (count <= 0) return;
            byte[] buffer = new byte[Math.Min(count, 64 * 1024)];
            long left = count;
            while (left > 0)
            {
                int n = (int)Math.Min(left, buffer.Length);
                w.Write(buffer, 0, n);
                left -= n;
            }
        }

        private static long Align(long x, uint a)
        {
            if (a == 0) return x;
            long rem = x % a;
            return rem == 0 ? x : x + (a - rem);
        }

        private static long Pad(long size, uint alignment) => Align(size, alignment);
    }

    /// <summary>
    /// Временный каталог для GGUF-фикстур. Удаляется вместе с содержимым при Dispose.
    /// Файлы реальных моделей (<c>W:\LLStudio\Models</c>) тесты не читают и не пишут.
    /// </summary>
    internal sealed class TempDir : IDisposable
    {
        public string Dir { get; }

        public TempDir(string prefix = "llmscan-gguf")
        {
            Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Dir);
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Dir))
                    System.IO.Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // best-effort: не роняем тест из-за занятого файла
            }
        }
    }
}
