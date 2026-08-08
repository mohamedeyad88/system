namespace Apex.Core.Models.PaperCutting
{
    /// <summary>
    /// A single product entry in a multi-product cutting job.
    /// Dimensions are expressed in the job's shared <see cref="MeasurementUnit"/>
    /// (carried by <see cref="PaperCuttingInput.Unit"/>), so this type stays unit-agnostic.
    ///
    /// This is the reusable building block for both cutting modes:
    ///   • Separate mode  – each product is optimized on its own sheets.
    ///   • Nested mode    – several products share one raw sheet (future phase).
    /// </summary>
    public class ProductSpec
    {
        /// <summary>Display name / label for the product (e.g. "كرت شخصي").</summary>
        public string Name { get; set; } = "";

        /// <summary>Finished product width (after trim), in the job unit.</summary>
        public double Width { get; set; }

        /// <summary>Finished product height (after trim), in the job unit.</summary>
        public double Height { get; set; }

        /// <summary>Total finished pieces required for this product.</summary>
        public int Quantity { get; set; }
    }
}
