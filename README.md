``GetVersionHelper(currentBranch)``
Then call ``GetNextVersion(defaultVersion)`` and this will return a ``VersionResult``
``VersionResult.RootVersion`` has an assembly safe version like 2.3.4
``VersionResult.FullVersion`` has the full version like 2.3.4-somerelease
``VersionResult.IsPreRelease`` lets you know if it is a prerelease (not build off default/master)

``GetNextVersionInfo(currentBranch, defaultVersion)`` is a shortcut to the above if you do not need access to the VersionHelper class.


