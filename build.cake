#addin nuget:https://ci.appveyor.com/nuget/cake-utility-4ufl9hamniq3/?package=Cake.Utility
//#addin cake.slack

var buildHelper = GetVersionHelper(AppVeyor.Environment.Repository.Branch);
var verInfo = buildHelper.GetNextVersion("1.0.0");
buildHelper.SetNextVersion(verInfo);

Task("Patch-Assembly-Info")
    .WithCriteria(() => buildHelper.IsCiBuildEnvironment)
    .Does(() =>
{
	var assemblyFiles = GetFiles("./**/AssemblyInfo.cs");
    foreach( var file in assemblyFiles) {
        if (file.ToString().Contains("packages/"))
			continue;
		var assemblyInfo = ParseAssemblyInfo(file);
		Information("Creating "+file);
		CreateAssemblyInfo(file, new AssemblyInfoSettings
		{
			Product = assemblyInfo.Product,
			Version = verInfo.RootVersion,
			FileVersion = verInfo.RootVersion,
			InformationalVersion = verInfo.FullVersion,
		});
    }
});

RunTarget("Patch-Assembly-Info");