using System.Collections.Generic;

namespace Matcher;

public class Mappings {
	public required string NamespaceA;
	public required string NamespaceB;
	public List<ClassMapping> Classes = new();
}

public class ClassMapping {
	public required string ClassNameA;
	public string? ClassNameB;
	public List<FieldMapping> Fields = new();
	public List<MethodMapping> Methods = new();
	public List<GenericParameterMapping> GenericParameters = new();
}

public class FieldMapping {
	public required string FieldTypeA;
	public required string FieldNameA;
	public string? FieldNameB;
}

public class MethodMapping {
	public required string ReturnTypeA;
	public required string MethodNameA;
	public string? MethodNameB;
	public List<MethodParameterMapping> Parameters = new();
	public List<GenericParameterMapping> GenericParameters = new();
}

public class MethodParameterMapping {
	public required uint ParameterIndex;
	public required string ParameterTypeA;
	public required string ParameterNameA;
	public string? ParameterNameB;
}

// TODO method locals?

public class GenericParameterMapping {
	// TODO could retain additional generic parameter info?
	public required string GenericNameA;
	public string? GenericNameB;
}
