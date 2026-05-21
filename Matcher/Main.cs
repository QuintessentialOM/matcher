global using System;
global using System.Collections.Generic;
global using Mono.Collections.Generic;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher;

public static class MatcherMain {
	public static void Main(string[] args) {
		TypeClassifier.init();
		MethodClassifier.init();
		FieldClassifier.init();
		MethodParamClassifier.init();
		var moduleA = ModuleDefinition.ReadModule("Lightning_old.exe");
		var moduleB = ModuleDefinition.ReadModule("Lightning_ce_skew_polymers.exe");
		var matcher = new Matcher();
		matcher.Init(moduleA, moduleB,
				"#=qb3HWBkVlFVubfVOAwuy8rw==",
				"#=qDID3KRmTOKqTiWqrwHq$pA==",
				["strings_old.csv"], ["strings_ce_skew_polymers.csv"]);
		Console.WriteLine(matcher.getStatus(false));
		matcher.autoMatchAll(n => Console.WriteLine(n));
		foreach (var cls in matcher.env.envA.types.Keys) {
			if (matcher.env.envA.types[cls].hasMatch() && !Matcher.NonObfuscatedPattern.IsMatch(cls))
				Console.WriteLine(cls);
		}
		Console.WriteLine(matcher.getStatus(false));
	}
}
