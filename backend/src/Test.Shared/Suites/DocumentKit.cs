namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Builds tiny test documents: a one-page PDF with a line of text (valid xref) and WebVTT caption files.
    /// </summary>
    internal static class DocumentKit
    {
        internal static byte[] Pdf(string text)
        {
            string safe = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            string stream = "BT /F1 18 Tf 72 720 Td (" + safe + ") Tj ET";
            List<string> objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
                "<< /Length " + Encoding.ASCII.GetByteCount(stream).ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + stream + "\nendstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
            };
            StringBuilder sb = new StringBuilder("%PDF-1.4\n");
            List<int> offsets = new List<int>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
                sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }
            int xref = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
            foreach (int o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        internal static byte[] Vtt(params string[] cues)
        {
            StringBuilder sb = new StringBuilder("WEBVTT\n\nNOTE generated for a test\n\n");
            for (int i = 0; i < cues.Length; i++)
            {
                sb.Append("cue-").Append(i + 1).Append('\n');
                sb.Append("00:00:").Append((i * 2).ToString("D2", CultureInfo.InvariantCulture)).Append(".000 --> 00:00:").Append((i * 2 + 1).ToString("D2", CultureInfo.InvariantCulture)).Append(".500\n");
                sb.Append("<v Speaker>").Append(cues[i]).Append("</v>\n\n");
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
