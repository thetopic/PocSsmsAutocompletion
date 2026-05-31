using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class ProcedureInfo {
        public string                      Schema        { get; }
        public string                      ProcedureName { get; }
        public IReadOnlyList<ParameterInfo> Parameters   { get; }

        public ProcedureInfo(string schema, string procedureName, IReadOnlyList<ParameterInfo> parameters) {
            Schema        = schema;
            ProcedureName = procedureName;
            Parameters    = parameters;
        }

        public override string ToString() =>
            Schema == "dbo" ? ProcedureName : $"{Schema}.{ProcedureName}";
    }
}
