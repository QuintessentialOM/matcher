using Mono.Cecil;

namespace Matcher;

public class Mappings {
	public required string NamespaceA;
	public required string NamespaceB;
	public int nextClassIndex;
	public int nextEnumIndex;
	public int nextInterfaceIndex;
	public int nextStructIndex;
	public int nextDelegateIndex;
	public int nextMethodIndex;
	public int nextFieldIndex;
	public int nextGenericIndex;
	public int nextParamIndex;
	public List<ClassMapping> Classes = [];

	public string GetNextTypeIntermediaryName(TypeDefinition type) {
		string name;
		if(type.BaseType?.FullName?.Equals("System.MulticastDelegate") ?? false) {
			name = "delegate_" + nextDelegateIndex;
			nextDelegateIndex++;
		} else if(type.IsInterface) {
			name = "interface_" + nextInterfaceIndex;
			nextInterfaceIndex++;
		} else if(type.IsEnum) {
			name = "enum_" + nextEnumIndex;
			nextEnumIndex++;
		} else if(type.IsValueType) {
			name = "struct_" + nextStructIndex;
			nextStructIndex++;
		} else {
			name = "class_" + nextClassIndex;
			nextClassIndex++;
		}
		return name;
	}

	public string GetNextMethodIntermediaryName() {
		string name = "method_" + nextMethodIndex;
		nextMethodIndex++;
		return name;
	}

	public string GetNextFieldIntermediaryName() {
		string name = "field_" + nextFieldIndex;
		nextFieldIndex++;
		return name;
	}

	public string GetNextGenericIntermediaryName() {
		string name = "generic_" + nextGenericIndex;
		nextGenericIndex++;
		return name;
	}

	public string GetNextParamIntermediaryName() {
		string name = "param_" + nextParamIndex;
		nextParamIndex++;
		return name;
	}
}

public class ClassMapping {
	public required string ClassFullNameA; // Includes containing types for nested types
	public string? ClassNameB;
	public List<FieldMapping> Fields = [];
	public List<MethodMapping> Methods = [];
	public List<GenericParameterMapping> GenericParameters = [];
}

public class FieldMapping {
	public required string FieldNameA;
	public string? FieldNameB;
}

public class MethodMapping {
	public required string MethodNameA;
	public string? MethodNameB;
	public List<MethodParameterMapping> Parameters = [];
	public List<GenericParameterMapping> GenericParameters = [];
}

public class MethodParameterMapping {
	public required string ParameterNameA;
	public string? ParameterNameB;
}

// TODO method locals?

public class GenericParameterMapping {
	public required string GenericNameA;
	public string? GenericNameB;
}
