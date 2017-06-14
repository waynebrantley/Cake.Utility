#addin nuget:https://ci.appveyor.com/nuget/cake-utility-4ufl9hamniq3/?package=Cake.Utility 
//#addin nuget:?package=Cake.Utility

var buildHelper = GetVersionHelper();
var verInfo = buildHelper.GetNextVersion("1.0.0");
buildHelper.SetNextVersion(verInfo);

var solutionInfo = buildHelper.GetSolutionToBuild();

Task("Patch-Assembly-Info")
	.WithCriteria(() => buildHelper.IsCiBuildEnvironment)
	.Does(() =>
{
	buildHelper.PatchAllAssemblyInfo(verInfo, "");
});

Task("Restore-NuGet-Packages")
	.IsDependentOn("Patch-Assembly-Info")
	.Does(() =>
{
	NuGetRestore(solutionInfo.SolutionFileAndPath,
		new NuGetRestoreSettings{ Verbosity = buildHelper.NuGetLoggingLevel }  //Normal, Quiet, Detailed
	);
});

RunTarget("Restore-NuGet-Packages");