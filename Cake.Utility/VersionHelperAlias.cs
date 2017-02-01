using System;
using Cake.Common.Build;
using Cake.Core;
using Cake.Core.Annotations;

namespace Cake.Utility
{
    public static class VersionHelperAlias
    {
        [CakeMethodAlias]
        public static VersionHelper GetVersionHelper(this ICakeContext context, string branch)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return new VersionHelper(context.Environment, context.Log, context.Arguments, context.TeamCity(), context.AppVeyor(), context.Globber, context.FileSystem, branch);
        }

        [CakeMethodAlias]
        public static VersionResult GetNextVersionInfo(this ICakeContext context, string branch, string defaultVersion)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var versionInfo =new VersionHelper(context.Environment, context.Log, context.Arguments, context.TeamCity(), context.AppVeyor(), context.Globber, context.FileSystem, branch);
            return versionInfo.GetNextVersion(defaultVersion);
        }
    }
}
