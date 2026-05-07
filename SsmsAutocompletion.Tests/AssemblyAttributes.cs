using System.Runtime.CompilerServices;

// Allow Moq's Castle DynamicProxy to create proxies for internal interfaces
// compiled into this test assembly (IDatabaseMetadata, IAliasExtractor, etc.)
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
