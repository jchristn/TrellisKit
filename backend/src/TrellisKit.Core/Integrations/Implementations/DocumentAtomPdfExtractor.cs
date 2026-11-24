namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using DocumentAtom.Core.Atoms;
    using DocumentAtom.Documents.Pdf;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// PDF text extraction through DocumentAtom (resolved decision 18). DocumentAtom's PDF processor is synchronous and
    /// reads from a file, so each extraction writes the bytes to a private temporary file in
    /// <see cref="TempDirectory"/>, runs on a worker thread, materializes the atoms before the processor is disposed,
    /// and deletes the file. Images are not OCRed (ExtractAtomsFromImages is false).
    /// Thread safety: safe to call concurrently (each call uses its own processor and file).
    /// </summary>
    public class DocumentAtomPdfExtractor : IDocumentTextExtractor
    {
        #region Public-Members

        /// <summary>
        /// Extractor name.
        /// </summary>
        public string Name
        {
            get { return "documentatom-pdf"; }
        }

        /// <summary>
        /// Directory of the temporary files. Default: the system temporary directory. Never null.
        /// </summary>
        public string TempDirectory
        {
            get { return _TempDirectory; }
            set { _TempDirectory = String.IsNullOrWhiteSpace(value) ? Path.GetTempPath() : value; }
        }

        #endregion

        #region Private-Members

        private string _TempDirectory = Path.GetTempPath();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Supports(string contentType)
        {
            return String.Equals((contentType ?? String.Empty).Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public async Task<ExtractedDocument> ExtractAsync(byte[] data, string contentType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            Directory.CreateDirectory(_TempDirectory);
            string path = Path.Combine(_TempDirectory, "trelliskit-pdf-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                await File.WriteAllBytesAsync(path, data, token).ConfigureAwait(false);
                List<Atom> atoms = await Task.Run(() =>
                {
                    using (PdfProcessor processor = new PdfProcessor(new PdfProcessorSettings { ExtractAtomsFromImages = false }))
                    {
                        return processor.Extract(path).ToList();
                    }
                }, token).ConfigureAwait(false);
                ExtractedDocument document = new ExtractedDocument { Extractor = Name };
                foreach (Atom atom in atoms) Collect(atom, document.Segments);
                return document;
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not IOException && e is not UnauthorizedAccessException)
            {
                throw new InvalidOperationException("The PDF could not be read: " + e.Message, e);
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // A leftover temporary file is harmless.
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void Collect(Atom atom, List<ExtractedSegment> segments)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(atom.Text)) parts.Add(atom.Text.Trim());
            if (atom.OrderedList != null) parts.AddRange(atom.OrderedList.Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()));
            if (atom.UnorderedList != null) parts.AddRange(atom.UnorderedList.Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()));
            if (parts.Count > 0) segments.Add(new ExtractedSegment { Text = String.Join("\n", parts), PageNumber = atom.PageNumber });
            if (atom.Quarks != null) foreach (Atom quark in atom.Quarks.Where(q => q != null && String.IsNullOrWhiteSpace(atom.Text))) Collect(quark, segments);
        }

        #endregion
    }
}
