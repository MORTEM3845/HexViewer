using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HexViewer.Items
{
    public static class FormatHex
    {
        private static readonly char[] HexLo = "0123456789ABCDEF".ToCharArray();

        public static byte[] ToArrayFast(IList<byte> list)
        {
            if (list is byte[] arr) return arr;
            var res = new byte[list.Count];
            for (int i = 0; i < res.Length; i++) res[i] = list[i];
            return res;
        }

        /// <summary>
        /// Конвертация в HEX-блок: байты верхним регистром через пробел,
        /// перенос строки после каждых 32 байтов.
        /// </summary>
        public static string BytesToHexBlockString(IList<byte> bytes)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < bytes.Count; i++)
            {
                sb.Append(bytes[i].ToString("X2"));

                if ((i + 1) % 32 == 0)
                {
                    sb.AppendLine(); // перевод строки каждые 32 байта
                }
                else
                {
                    sb.Append(' '); // обычный пробел между байтами
                }
            }

            return sb.ToString().TrimEnd();
        }


        public static string BytesToHexString(IList<byte> bytes)
        {
            return string.Create(bytes.Count * 2, bytes, (span, data) =>
            {
                int pos = 0;
                foreach (var b in data)
                {
                    span[pos++] = HexLo[b >> 4];
                    span[pos++] = HexLo[b & 0xF];
                }
            });
        }

    }
}
