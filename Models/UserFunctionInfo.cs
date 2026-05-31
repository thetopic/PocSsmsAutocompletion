using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal enum UserFunctionType { Scalar, TableValued, InlineTableValued }

    internal sealed class UserFunctionInfo {
        public string                      Schema        { get; }
        public string                      FunctionName  { get; }
        public UserFunctionType            FunctionType  { get; }
        public IReadOnlyList<ParameterInfo> Parameters   { get; }

        public UserFunctionInfo(
            string schema, string functionName,
            UserFunctionType functionType, IReadOnlyList<ParameterInfo> parameters) {
            Schema       = schema;
            FunctionName = functionName;
            FunctionType = functionType;
            Parameters   = parameters;
        }

        public bool IsTableValued =>
            FunctionType == UserFunctionType.TableValued ||
            FunctionType == UserFunctionType.InlineTableValued;

        public override string ToString() =>
            Schema == "dbo" ? FunctionName : $"{Schema}.{FunctionName}";
    }
}
