using System;
using System.Linq;
using Cake.Common.Build.AppVeyor;
using Cake.Common.Build.TeamCity;
using Cake.Common.Solution.Project.Properties;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;

namespace Cake.Utility
{
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
        public const string DefaultBuildVersionArgumentName = "buildVersion";
        public const string DefaultMasterBaseVersionEnvironmentVariable = "RootVersion.Master";
        public const string DefaultPreReleaseBaseVersionEnvironmentVariable = "RootVersion.Feature";
        public const string DefaultDefaultBranchName = "master";
        public VersionHelper(ICakeEnvironment environment, ICakeLog log, ICakeArguments arguments, ITeamCityProvider teamCityProvider,
                             IAppVeyorProvider appVeyorProvider, IGlobber globber, IFileSystem fileSystem)
        {
            _environment = environment;
            _log = log;
            _arguments = arguments;
            _teamCityProvider = teamCityProvider;
            _appVeyorProvider = appVeyorProvider;
            _globber = globber;
            _fileSystem = fileSystem;
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (log == null)
                throw new ArgumentNullException(nameof(log));
            if (IsAppVeyor)
            {
                Branch = _appVeyorProvider.Environment.Repository.Branch;
                CommitMessageShort = _appVeyorProvider.Environment.Repository.Commit.Message;
            }
        }

        public string BuildVersionArgumentName { get; set; } = DefaultBuildVersionArgumentName;
        public string MasterBaseVersionEnvironmentVariable { get; set; } = DefaultMasterBaseVersionEnvironmentVariable;
        public string PreReleaseBaseVersionEnvironmentVariable { get; set; } = DefaultPreReleaseBaseVersionEnvironmentVariable;
        public string DefaultBranchName { get; set; } = DefaultDefaultBranchName;

        public string Branch { get; set; } = string.Empty;
        public string CommitMessageShort { get; set; }
        public bool IsTeamCity => _teamCityProvider.IsRunningOnTeamCity;
        public bool IsAppVeyor => _appVeyorProvider.IsRunningOnAppVeyor;
        public bool IsInteractiveBuild => !IsAppVeyor && !IsTeamCity;
        public bool IsCiBuildEnvironment => IsAppVeyor || IsTeamCity;
        public bool IsPreRelease => string.Compare(Branch, DefaultBranchName, StringComparison.OrdinalIgnoreCase) != 0;
        public bool ShouldDeploy => IsCiBuildEnvironment && !IsPreRelease && !IsPullRequest;
        public bool IsPullRequest => IsAppVeyor && _appVeyorProvider.Environment.PullRequest.IsPullRequest;
        public string BuildEnvironmentName => IsAppVeyor ? "AppVeyor" : IsTeamCity ? "TeamCity" : "Interactive";

        public string GetBaseVersionString(string defaultVersion)
        {
            if (_arguments.HasArgument(BuildVersionArgumentName))
                return _arguments.GetArgument(BuildVersionArgumentName);
            if (IsAppVeyor)
            {
                string version = _appVeyorProvider.Environment.Build.Version;
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
                result.FullVersion = result.RootVersion + extraLabel;
            }
            else
                result.FullVersion = result.RootVersion;
            return result;
        }

        public void PatchAllAssemblyInfo(VersionResult versionInfo, string copyrightText)
        {
            var parser = new AssemblyInfoParser(_fileSystem, _environment);
            var creator = new AssemblyInfoCreator(_fileSystem, _environment, _log);
            var assemblyFiles = _globber.GetFiles("./**/AssemblyInfo.cs");
            foreach (var file in assemblyFiles)
            {
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

        public SolutionInfoResult GetSolutionToBuild()
        {
            var files = _globber.GetFiles("./**/*.sln").ToList();
            if (files.Count == 0)
                throw new Exception("Solution file not found");
            if (files.Count > 1)
                _log.Warning("Multiple solution files found");
            return new SolutionInfoResult
            {
                SolutionFileAndPath = files[0].FullPath,
                SolutionFilename = files[0].GetFilename().ToString()
            };
        }
    }
}
