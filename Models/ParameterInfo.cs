namespace SsmsAutocompletion {

    internal sealed class ParameterInfo {
        public string Name       { get; }
        public string DataType   { get; }
        public bool   IsOutput   { get; }
        public bool   HasDefault { get; }

        public ParameterInfo(string name, string dataType, bool isOutput, bool hasDefault) {
            Name       = name;
            DataType   = dataType;
            IsOutput   = isOutput;
            HasDefault = hasDefault;
        }
    }
}
