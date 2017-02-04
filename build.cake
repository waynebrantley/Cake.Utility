#addin nuget:https://ci.appveyor.com/nuget/cake-utility-4ufl9hamniq3/?package=Cake.Utility

var buildHelper = GetVersionHelper();
var verInfo = buildHelper.GetNextVersion("1.0.0");
buildHelper.SetNextVersion(verInfo);

Task("Patch-Assembly-Info")
	.WithCriteria(() => buildHelper.IsCiBuildEnvironment)
	.Does(() =>
{
	buildHelper.PatchAllAssemblyInfo(verInfo, "");
});

RunTarget("Patch-Assembly-Info");