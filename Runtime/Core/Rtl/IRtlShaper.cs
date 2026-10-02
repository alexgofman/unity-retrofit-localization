namespace RetrofitLocalization
{
    /// <summary>
    /// Turns Arabic-script text from the order it is typed in (logical order) into the order a
    /// left-to-right text renderer has to draw it in (visual order), with each letter replaced by
    /// the presentation form that joins correctly to its neighbours.
    ///
    /// The contract is deliberately narrow so that any shaping library can sit behind it:
    /// the input is a single line, contains no rich-text tags, and is to be laid out as a
    /// right-to-left paragraph. <see cref="RtlText"/> takes care of lines and tags and calls the
    /// shaper once per plain run.
    /// </summary>
    public interface IRtlShaper
    {
        /// <summary>Returns <paramref name="logicalRun"/> in visual order with joined letter forms.</summary>
        string Shape(string logicalRun);
    }
}
