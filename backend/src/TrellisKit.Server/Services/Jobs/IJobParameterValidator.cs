namespace TrellisKit.Server.Services.Jobs
{
    using System;

    /// <summary>Optional validation before a manual job run is queued. Implementations are thread safe.</summary>
    public interface IJobParameterValidator
    {
        /// <summary>Validate handler parameters before creating a run.</summary>
        /// <param name="parameters">JSON object string, or null for defaults.</param>
        /// <exception cref="ArgumentException">Parameters are invalid.</exception>
        void ValidateParameters(string? parameters);
    }
}
