using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cake.Common.Build.AppVeyor;
using Cake.Common.Build.TeamCity;
using Cake.Common.IO;
using Cake.Common.Solution.Project.Properties;
using Cake.Common.Tools.MSBuild;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Pack;
using Cake.Common.Tools.NUnit;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.IO.NuGet;
using Cake.Core.Tooling;

namespace Cake.Utility
{

    public class MSBuildSettingsBuilder
    {
        public bool OctoPackToArtifacts { get; set; }
        public bool PublishOnBuild { get; set; }
        public string PublishProfile { get; set; }
    }

    public class VersionResult
    {
        public string RootVersion { get; set; }
        public string FullVersion { get; set; }
        public bool IsPreRelease { get; set; }
    }

    public class SolutionInfoResult
    {
        public string SolutionFileAndPath { get; set; }
        public string SolutionFilename { get; set; }

    }

    public class MatchResult
    {
        public bool Success { get; set; }
        public GroupCollection Groups { get; set; }
    }

    //TeamCity project 'build number' should be set to {0}, so it is just an incrementing number.
    //This will be added to the base versions defined in environment variables.
    public class VersionHelper
    {
        private readonly ICakeEnvironment _environment;
        private readonly ICakeLog _log;
        private readonly ICakeArguments _arguments;
        private readonly ITeamCityProvider _teamCityProvider;
        private readonly IAppVeyorProvider _appVeyorProvider;
        private readonly IGlobber _globber;
        private readonly IFileSystem _fileSystem;
        private readonly IProcessRunner _processRunner;
        private readonly IToolLocator _tools;
        public static Regex CommitMessageRegex;
        private readonly bool _isDefaultLoggingLevel = true;

        public const string DefaultBuildVersionArgumentName = "buildVersion";
        public const string DefaultMasterBaseVersionEnvironmentVariable = "RootVersion.Master";
        public const string DefaultPreReleaseBaseVersionEnvironmentVariable = "RootVersion.Feature";
        public const string DefaultDefaultBranchName = "master";

        static VersionHelper()
        {
            //\[(?<command>(?i)Deploy|Fred) +(?<argument>[\w\.]+)\]+
            //http://regexstorm.net/tester?p=%5c%5b%28%3f%3ccommand%3e%28%3fi%29Deploy%7cFred%29+%2b%28%3f%3cargument%3e%5b%5cw%5c.%5d%2b%29%5c%5d%2b&i=%5bdeploy+uat4%5d+%0d%0a%5bFred+uat4%5d+%0d%0a%5bDePloy+uat4%5d+%0d%0a%5bfrEd+uat4%5d+%0d%0a%5bdeploy+uat4%5d+%0d%0a%5bFred++uat4%5d+%0d%0a%5bDePloy+++uat4%5d+%0d%0a%5bfrEd+++uat4%5d+%0d%0a
            string[] commands = { "Deploy" };
            CommitMessageRegex = new Regex($@"\[(?<command>(?i){string.Join("|", commands)}) +(?<argument>[\w\.]+)\]+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
        public VersionHelper(ICakeEnvironment environment, ICakeLog log, ICakeArguments arguments, ITeamCityProvider teamCityProvider,
                             IAppVeyorProvider appVeyorProvider, IGlobber globber, IFileSystem fileSystem, IProcessRunner processRunner, IToolLocator tools)
        {
            _environment = environment;
            _log = log;
            _arguments = arguments;
            _teamCityProvider = teamCityProvider;
            _appVeyorProvider = appVeyorProvider;
            _globber = globber;
            _fileSystem = fileSystem;
            _processRunner = processRunner;
            _tools = tools;
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (log == null)
                throw new ArgumentNullException(nameof(log));

            Configuration = environment.GetEnvironmentVariable("CONFIGURATION");
            if (string.IsNullOrWhiteSpace(Configuration))
                Configuration = _arguments.HasArgument("configuration") ? _arguments.GetArgument("configuration") : "Release";

            string envLogging = environment.GetEnvironmentVariable("LOGGINGLEVEL");
            if (!string.IsNullOrWhiteSpace(envLogging))
            {
                Verbosity loggingEnum;
                if (Enum.TryParse(envLogging, true, out loggingEnum))
                {
                    log.Verbosity = loggingEnum;
                    log.Information($"Logging Level: {loggingEnum}", Verbosity.Quiet);
                    _isDefaultLoggingLevel = false;
                }

            }
            else
                _isDefaultLoggingLevel = !arguments.HasArgument("verbosity");


            CommitMessageMatches = new MatchResult { Success = false };
            if (IsAppVeyor)
            {
                Branch = _appVeyorProvider.Environment.Repository.Branch;
                CommitMessageShort = _appVeyorProvider.Environment.Repository.Commit.Message;
                var match = CommitMessageRegex.Match(_appVeyorProvider.Environment.Repository.Commit.ExtendedMessage);
                CommitMessageMatches = new MatchResult { Success = match.Success, Groups = match.Groups };
                _log.Debug($"Branch:{Branch}");
                _log.Debug($"Commit Msg Short:{CommitMessageShort}");
                _log.Debug($"Commit Msg Extended:{_appVeyorProvider.Environment.Repository.Commit.ExtendedMessage}");
                _log.Debug($"Commit Message Cmd Match:{CommitMessageMatches.Success}");
                if (_log.Verbosity >= Verbosity.Verbose && CommitMessageMatches.Success)
                {
                    _log.Debug("RegEx Group Matches");
                    foreach (string groupName in CommitMessageRegex.GetGroupNames())
                    {
                        _log.Debug($"{groupName} : {CommitMessageMatches.Groups[groupName].Value}");
                    }
                }
            }
        }

        public string BuildVersionArgumentName { get; set; } = DefaultBuildVersionArgumentName;
        public string MasterBaseVersionEnvironmentVariable { get; set; } = DefaultMasterBaseVersionEnvironmentVariable;
        public string PreReleaseBaseVersionEnvironmentVariable { get; set; } = DefaultPreReleaseBaseVersionEnvironmentVariable;
        public string DefaultBranchName { get; set; } = DefaultDefaultBranchName;

        public string Branch { get; set; } = string.Empty;
        public string Configuration { get; set; }
        public string CommitMessageShort { get; set; }
        public bool IsTeamCity => _teamCityProvider.IsRunningOnTeamCity;
        public bool IsAppVeyor => _appVeyorProvider.IsRunningOnAppVeyor;
        public bool IsInteractiveBuild => !IsAppVeyor && !IsTeamCity;
        public bool IsCiBuildEnvironment => IsAppVeyor || IsTeamCity;
        public bool IsPreRelease => string.Compare(Branch, DefaultBranchName, StringComparison.OrdinalIgnoreCase) != 0;
        public bool ShouldDeploy => IsCiBuildEnvironment && !IsPreRelease && !IsPullRequest;
        public bool IsPullRequest => IsAppVeyor && _appVeyorProvider.Environment.PullRequest.IsPullRequest;
        public string BuildEnvironmentName => IsAppVeyor ? "AppVeyor" : IsTeamCity ? "TeamCity" : "Interactive";

        public bool AutoDeploy => IsCiBuildEnvironment && IsPreRelease && !IsPullRequest && CommitMessageMatches.Success;
        public string AutoDeployTarget => CommitMessageMatches.Success ? CommitMessageMatches.Groups["argument"].Value.ToLower() : string.Empty;

        public NuGetVerbosity NuGetLoggingLevel => _isDefaultLoggingLevel || _log.Verbosity == Verbosity.Normal ? NuGetVerbosity.Normal : (_log.Verbosity < Verbosity.Normal ? NuGetVerbosity.Quiet : NuGetVerbosity.Detailed);
        public Verbosity MsBuildLoggingLevel => _isDefaultLoggingLevel ? Verbosity.Minimal : _log.Verbosity;

        public MatchResult CommitMessageMatches { get; }

        public string GetBaseVersionString(string defaultVersion)
        {
            if (_arguments.HasArgument(BuildVersionArgumentName))
                return _arguments.GetArgument(BuildVersionArgumentName);
            if (IsAppVeyor)
            {
                string version = _appVeyorProvider.Environment.Build.Version;
                _log.Verbose($"AppVeyor Build Version from Env Variable:{version}");
                return string.IsNullOrWhiteSpace(version) ? defaultVersion : version;
            }
            if (IsTeamCity)
            {
                string masterRootVersion = _environment.GetEnvironmentVariable(MasterBaseVersionEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(masterRootVersion))
                {
                    _log.Warning($"{MasterBaseVersionEnvironmentVariable} environment variable not defined.  Should be like 2.3 or something.  The first two parts of the version number for the 'default/production' builds");
                    return defaultVersion;
                }
                string preReleaseRootVersion = _environment.GetEnvironmentVariable(PreReleaseBaseVersionEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(preReleaseRootVersion))
                {
                    _log.Warning($"{PreReleaseBaseVersionEnvironmentVariable} environment variable not defined.  Should be like 1.3 or something.  The first two parts of the version number for the 'prerelease' builds");
                    return defaultVersion;
                }
                return $"{(IsPreRelease ? preReleaseRootVersion : masterRootVersion)}.{_environment.GetEnvironmentVariable("BUILD_NUMBER")}";
            }
            return defaultVersion;
        }

        public void SetNextVersion(VersionResult version)
        {
            _log.Information($"Building {version.FullVersion}");
            if (IsInteractiveBuild)
                _log.Information($"Interacitve Build Mode");
            else if (IsAppVeyor)
                _appVeyorProvider.UpdateBuildVersion(version.FullVersion);
            else if (IsTeamCity)
                _teamCityProvider.SetBuildNumber(version.FullVersion);
        }

        public VersionResult GetNextVersion(string defaultVersion)
        {
            var result = new VersionResult
            {
                RootVersion = GetBaseVersionString(defaultVersion),
                IsPreRelease = IsPreRelease
            };
            if (IsPreRelease)
            {
                string extraLabel = "-" + Branch.Replace("_", "");
                if (extraLabel.Length > 20)
                    extraLabel = extraLabel.Substring(0, 20);
                _log.Verbose($"PreRelease detected:{extraLabel}");
                result.FullVersion = result.RootVersion + extraLabel;
            }
            else
                result.FullVersion = result.RootVersion;
            return result;
        }

        private bool PatchSingleSolutionVersionFile(VersionResult versionInfo, string copyrightText)
        {
            var solutionVersion = _globber.GetFiles("./**/SolutionVersion.cs").ToList();
            if (solutionVersion.Count == 1)
            {
                var file = solutionVersion[0];
                var parser = new AssemblyInfoParser(_fileSystem, _environment);
                var creator = new AssemblyInfoCreator(_fileSystem, _environment, _log);
                _log.Information($"Patching single solution version file:{file}");
                var assemblyInfo = parser.Parse(file);
                creator.Create(file, new AssemblyInfoSettings
                {
                    Product = assemblyInfo.Product,
                    Version = versionInfo.RootVersion,
                    FileVersion = versionInfo.RootVersion,
                    InformationalVersion = versionInfo.FullVersion,
                    Copyright = string.Format(copyrightText, DateTime.Now.Year)
                });
                return true;
            }
            return false;
        }
        public void PatchAllAssemblyInfo(VersionResult versionInfo, string copyrightText)
        {
            if (PatchSingleSolutionVersionFile(versionInfo, copyrightText))
                return;
            var parser = new AssemblyInfoParser(_fileSystem, _environment);
            var creator = new AssemblyInfoCreator(_fileSystem, _environment, _log);
            var assemblyFiles = _globber.GetFiles("./**/AssemblyInfo.cs");
            foreach (var file in assemblyFiles)
            {
                _log.Verbose($"Possibly file to patch:{file}");
                if (file.ToString().Contains("packages/"))
                    continue;
                var assemblyInfo = parser.Parse(file);
                _log.Information("Creating " + file);
                creator.Create(file, new AssemblyInfoSettings
                {
                    Product = assemblyInfo.Product,
                    Version = versionInfo.RootVersion,
                    FileVersion = versionInfo.RootVersion,
                    InformationalVersion = versionInfo.FullVersion,
                    Copyright = string.Format(copyrightText, DateTime.Now.Year)
                });
            }
        }

        private void NUnit2Test(List<FilePath> assemblies, FilePath outputFile, string excludeFilter)
        {
            var settings = new NUnitSettings
            {
                NoLogo = true,
                NoResults = IsInteractiveBuild,
                ResultsFile = outputFile,
                Exclude = excludeFilter
            };

            var runner = new NUnitRunner(_fileSystem, _environment, _processRunner, _tools);
            runner.Run(assemblies, settings);
        }

        private void NUnit3Test(List<FilePath> assemblies, FilePath outputFile, string whereFilter)
        {
            var settings = new NUnit3Settings
            {
                NoHeader = true,
                NoResults = IsInteractiveBuild,
                Verbose = _log.Verbosity > Verbosity.Normal,
                OutputFile = outputFile,
                ResultFormat = "AppVeyor",
                Where = whereFilter
            };
            var runner = new NUnit3Runner(_fileSystem, _environment, _processRunner, _tools);
            runner.Run(assemblies, settings);
        }

        public void RunNUnitTests(AppVeyorTestResultsType testType, string whereFilter = null)
        {

            var assemblies = _globber.GetFiles("./**/bin/" + Configuration + "/*.Tests.dll").ToList();
            assemblies = assemblies.Union(_globber.GetFiles("./**/bin/" + Configuration + "/*.Test.dll")).ToList();
            if (assemblies.Count == 0)
            {
                _log.Error("No Tests Found");
                return;
            }

            foreach (var file in assemblies)
            {
                _log.Verbose($"Using test asembly:{file.FullPath}");
            }

            //NUnit test runners output if a file is given.  Should only output if NoResults is false...but it doesnt..
            var testResultsFile = IsInteractiveBuild ? null : new FilePath("./TestResult.xml");
            try
            {
                if (testType == AppVeyorTestResultsType.NUnit3)
                    NUnit3Test(assemblies, testResultsFile, whereFilter);
                else
                    NUnit2Test(assemblies, testResultsFile, whereFilter);
            }
            finally
            {
                if (IsAppVeyor)
                    _appVeyorProvider.UploadTestResults(testResultsFile, testType);
            }
        }

        public void UploadArtifactsFolder()
        {
            var artifacts = _globber.GetFiles("./Artifacts/*.*").ToList();
            _log.Information($"Looking for artifacts in './Artifacts/*.*'");
            foreach (var artifact in artifacts)
            {
                _log.Information($"Found artifact '{artifact.FullPath}' - Uploading");
                _appVeyorProvider.UploadArtifact(artifact);
            }
        }


        public SolutionInfoResult GetSolutionToBuild(string fileName = null)
        {
            var files = _globber.GetFiles("./**/*.sln").ToList();
            if (files.Count == 0)
                throw new Exception("Solution file not found");
            if (files.Count > 1)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    _log.Warning("Multiple solution files found");
                    foreach (var file in files)
                    {
                        _log.Warning(file.FullPath);
                    }
                }
                else
                {
                    var matchFile = files.FirstOrDefault(f => string.Compare(f.GetFilename().ToString(), fileName, StringComparison.OrdinalIgnoreCase) == 0);
                    if (matchFile == null)
                        throw new Exception($"Solution file specified as {fileName} was not found");
                    return new SolutionInfoResult
                    {
                        SolutionFileAndPath = matchFile.FullPath,
                        SolutionFilename = matchFile.GetFilename().ToString()
                    };
                }
            }
            return new SolutionInfoResult
            {
                SolutionFileAndPath = files[0].FullPath,
                SolutionFilename = files[0].GetFilename().ToString()
            };
        }


        public MSBuildSettings GetMSBuildSettings(MSBuildSettingsBuilder settings)
        {

            var msBuildSettings = new MSBuildSettings
            {
                Verbosity = MsBuildLoggingLevel, //http://cakebuild.net/api/Cake.Core.Diagnostics/Verbosity/
                Configuration = Configuration,
                //ToolVersion = MSBuildToolVersion.VS2015,
                //PlatformTarget = PlatformTarget.MSIL
            };
            string artifactPath = new DirectoryPath("./Artifacts").MakeAbsolute(_environment).FullPath;
            if (settings.OctoPackToArtifacts)
            {
                msBuildSettings.Properties.Add("RunOctoPack", new List<string> { "true" });
                msBuildSettings.Properties.Add("OctoPackPublishPackageToFileShare", new List<string> { artifactPath });
                //msBuildSettings.Properties.Add("OctoPackEnforceAddingFiles", new List<string> { "true" });
            }

            if (settings.PublishOnBuild)
            {
                msBuildSettings.Properties.Add("DeployOnBuild", new List<string> { "true" });
                msBuildSettings.Properties.Add("PublishProfile", new List<string> { settings.PublishProfile });
                msBuildSettings.Properties.Add("ExcludeGeneratedDebugSymbol", new List<string> { "false" });
            }

            return msBuildSettings;
        }

        public MSBuildSettings GetMSBuildSettings()
        {
            return GetMSBuildSettings(new MSBuildSettingsBuilder());
        }

        public void CreatePackagesForAllNuSpecOutputToArtifactsFolder(string fullVersion)
        {
            var files = _globber.GetFiles("./**/*.nuspec").ToList();
            if (files.Count == 0)
                throw new Exception("No .nuspec files found to create packages from");

            var nuGetToolResolver = new NuGetToolResolver(_fileSystem, _environment, _tools);
            var packer = new NuGetPacker(_fileSystem, _environment, _processRunner, _log, _tools, nuGetToolResolver);

            var output = new DirectoryPath("./Artifacts");
            var directory = _fileSystem.GetDirectory(output);
            if (!directory.Exists)
                directory.Create();

            var nuGetPackSettings = new NuGetPackSettings
            {
                Version = fullVersion,
                OutputDirectory = output.FullPath,
                IncludeReferencedProjects = true,
                Properties = new Dictionary<string, string> { { "Configuration", Configuration } },
            };

            foreach (var nuspec in files)
            {
                _log.Information(nuspec.FullPath);
                var csproj = nuspec.ChangeExtension(".csproj");
                _log.Information(csproj.FullPath);
                packer.Pack(csproj.FullPath, nuGetPackSettings);
            }

        }

    }
}
