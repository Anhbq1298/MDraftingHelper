using System.Collections.Generic;
using System.Linq;

namespace MDraftingHeper.Core.Validation
{
    public sealed class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public bool IsValid => _errors.Count == 0;

        public ValidationResult Error(string message) { _errors.Add(message); return this; }
        public ValidationResult Warn(string message) { _warnings.Add(message); return this; }

        public ValidationResult Require(bool condition, string message)
        {
            if (!condition) _errors.Add(message);
            return this;
        }

        public void Merge(ValidationResult other)
        {
            _errors.AddRange(other._errors);
            _warnings.AddRange(other._warnings);
        }

        public string Summary => string.Join("\n", _errors.Concat(_warnings.Select(w => "Warning: " + w)));

        public override string ToString() => IsValid ? "Valid" : string.Join("; ", _errors);
    }
}
