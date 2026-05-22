global using System;
global using System.Collections.Generic;
global using Mono.Collections.Generic;
using Matcher.Matching.Classifier;
using Mono.Cecil;

namespace Matcher;

public static class MatcherMain {
	public static void Main(string[] args) {
		TypeClassifier.Init();
		MethodClassifier.Init();
		FieldClassifier.Init();
		MethodParamClassifier.Init();
		var moduleA = ModuleDefinition.ReadModule("Lightning_old.exe");
		var moduleB = ModuleDefinition.ReadModule("Lightning_ce_skew_polymers.exe");
		var matcher = new Matcher();
		matcher.Init(moduleA, moduleB,
				"#=qb3HWBkVlFVubfVOAwuy8rw==",
				"#=qDID3KRmTOKqTiWqrwHq$pA==",
				["strings_old.csv"], ["strings_ce_skew_polymers.csv"]);
		matcher.AutoMatchAll(Console.WriteLine);
	}
}
