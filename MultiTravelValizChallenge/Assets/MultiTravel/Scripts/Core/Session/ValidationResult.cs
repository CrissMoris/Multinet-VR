using System.Collections.Generic;

namespace MultiTravel.Core.Session
{
    /// <summary>
    /// Outcome of <see cref="ParticipantValidator.Validate"/>. <see cref="Errors"/> is keyed by field name
    /// (<see cref="ParticipantValidator.FieldFirstName"/> etc.) and holds Turkish messages for inline display.
    /// </summary>
    public sealed class ValidationResult
    {
        private readonly Dictionary<string, string> errors;

        public ValidationResult(Dictionary<string, string> errors, ParticipantInput normalized)
        {
            this.errors = errors ?? new Dictionary<string, string>();
            Normalized = normalized;
        }

        /// <summary>True when no field has an error.</summary>
        public bool IsValid => errors.Count == 0;

        /// <summary>Field name → Turkish error message.</summary>
        public IReadOnlyDictionary<string, string> Errors => errors;

        /// <summary>Trimmed / normalised copy of the input (only meaningful when <see cref="IsValid"/>).</summary>
        public ParticipantInput Normalized { get; }

        /// <summary>First error message in field order, or null when valid.</summary>
        public string FirstError
        {
            get
            {
                foreach (var field in ParticipantValidator.FieldOrder)
                {
                    if (errors.TryGetValue(field, out var message))
                    {
                        return message;
                    }
                }

                foreach (var pair in errors)
                {
                    return pair.Value;
                }

                return null;
            }
        }

        /// <summary>Returns the error message for a field, if any.</summary>
        public bool TryGetError(string field, out string message)
        {
            return errors.TryGetValue(field ?? string.Empty, out message);
        }
    }
}
