using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Matcher.Matching.Classifier;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Matcher.Matching;

public class FieldInstance : MatchableMember {
	public FieldDefinition? CecilField { get {
		return (FieldDefinition?) CecilMemberReference;
	} }
	private readonly string id;
	private FieldInstance? matchedField;
	private bool matchable = true;
	public TypeInstance fieldType;

	public readonly HashSet<MethodInstance> readRefs = [];
	public readonly HashSet<MethodInstance> writeRefs = [];

	[SetsRequiredMembers]
	public FieldInstance(LocalClassEnv env, TypeInstance containingType, FieldDefinition cecilField, int position, bool isNameObfuscated) : base(env, containingType, cecilField, position, isNameObfuscated) {
		id = GetId(cecilField.Name, cecilField.FieldType.Name);
		fieldType = env.GetCreateTypeInstance(cecilField.FieldType);
		fieldType.fieldTypeRefs.Add(this);
	}
	
	public override string GetId() {
		return id;
	}

	public override bool IsMatchable() {
		return matchable && ContainingType.IsMatchable();
	}

	public override bool SetMatchable(bool matchable) {
		if (!matchable && matchedField != null) return false;
		if (matchable && !ContainingType.IsMatchable()) return false;

		this.matchable = matchable;

		return true;
	}

	public override FieldInstance? GetMatch() {
		return matchedField;
	}

	public void SetMatch(FieldInstance? field) {
		if (field != null && Env == field.Env) throw new Exception("trying to match with field instance in same env");
		matchedField = field;
	}

	public static string GetId(string name, string desc) {
		return name + ";;" + desc;
	}

	private bool searchedForInitValue = false;
	private object? initValue = null;

	// TODO could also search for `newobj` opcodes calling no-arg constructors immediately before the store instr
	public object? TryFindFieldInitValue() {
		if (searchedForInitValue)
			return initValue;
		var constructorName = CecilField!.IsStatic ? ".cctor" : ".ctor";
		var targetOpcode = CecilField!.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld;
		List<object> candidateInitValues = [];
		foreach (var ctor in CecilField!.DeclaringType.Methods.Where(method => method.Name == constructorName)) {
			foreach (var maybeStoreInstr in ctor.Body.Instructions) {
				if (maybeStoreInstr.OpCode == targetOpcode && maybeStoreInstr.Operand == CecilField!) {
					var maybeConstInstr = maybeStoreInstr.Previous;
					if (maybeConstInstr == null) continue;
					var maybeIntValue = ClassifierUtil.getLdcI4Value(maybeConstInstr);
					if (maybeIntValue != null) {
						candidateInitValues.Add(maybeIntValue);
					} else if (maybeConstInstr.OpCode == OpCodes.Ldc_I8) {
						candidateInitValues.Add((long) maybeConstInstr.Operand);
					} else if (maybeConstInstr.OpCode == OpCodes.Ldc_R4) {
						candidateInitValues.Add((float) maybeConstInstr.Operand);
					} else if (maybeConstInstr.OpCode == OpCodes.Ldc_R8) {
						candidateInitValues.Add((double) maybeConstInstr.Operand);
					} else if (maybeConstInstr.OpCode == OpCodes.Ldstr) {
						candidateInitValues.Add((string) maybeConstInstr.Operand);
					}
				}
			}
		}

		// return null if nothing found or multiple possible values found
		if (candidateInitValues.Count != 1) {
			if (candidateInitValues.Count > 1) {
				// Console.WriteLine($"Multiple possible init values {string.Join(", ", candidateInitValues)} for field {CecilField!.FullName}");
			}
			initValue = null;
		} else {
			// Console.WriteLine($"Found init value {candidateInitValues.Single()} for field {CecilField!.FullName}");
			initValue = candidateInitValues.Single();
		}
		searchedForInitValue = true;
		return initValue;
	}
}
