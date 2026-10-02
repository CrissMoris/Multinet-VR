using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MultiTravel.EditorTools
{
    /// <summary>
    /// Plain, serialisable summary returned by every editor generator / configurator.
    /// Public fields only so it can be returned as-is from CLI commands (JSON) and logged from menu items.
    /// </summary>
    [Serializable]
    public sealed class GeneratorReport
    {
        public string Operation;
        public bool Success = true;
        public List<string> Created = new List<string>();
        public List<string> Updated = new List<string>();
        public List<string> Skipped = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();

        public GeneratorReport()
        {
        }

        public GeneratorReport(string operation)
        {
            Operation = operation;
        }

        public void AddError(string message)
        {
            Errors.Add(message);
            Success = false;
        }

        /// <summary>Appends every list of <paramref name="other"/> into this report (prefixing nothing).</summary>
        public void Merge(GeneratorReport other)
        {
            if (other == null)
            {
                return;
            }

            Created.AddRange(other.Created);
            Updated.AddRange(other.Updated);
            Skipped.AddRange(other.Skipped);
            Warnings.AddRange(other.Warnings);
            Errors.AddRange(other.Errors);
            Success &= other.Success;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("[MultiTravel] ").Append(Operation).Append(Success ? " OK" : " FAILED");
            sb.Append($" (created {Created.Count}, updated {Updated.Count}, skipped {Skipped.Count}, warnings {Warnings.Count}, errors {Errors.Count})");
            Append(sb, "Created", Created);
            Append(sb, "Updated", Updated);
            Append(sb, "Skipped", Skipped);
            Append(sb, "Warning", Warnings);
            Append(sb, "Error", Errors);
            return sb.ToString();
        }

        /// <summary>Logs the report with a severity matching its content.</summary>
        public void Log()
        {
            var text = ToString();
            if (Errors.Count > 0)
            {
                Debug.LogError(text);
            }
            else if (Warnings.Count > 0)
            {
                Debug.LogWarning(text);
            }
            else
            {
                Debug.Log(text);
            }
        }

        private static void Append(StringBuilder sb, string label, List<string> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append('\n').Append("  ").Append(label).Append(": ").Append(items[i]);
            }
        }
    }
}
