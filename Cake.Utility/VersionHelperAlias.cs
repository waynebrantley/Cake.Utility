using System;
using Cake.Common.Build;
using Cake.Core;
using Cake.Core.Annotations;

namespace Cake.Utility
{
    public static class VersionHelperAlias
    {
        [CakeMethodAlias]
        public static VersionHelper GetVersionHelper(this ICakeContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return new VersionHelper(context.Environment, context.Log, context.Arguments, 
                                     context.AppVeyor(), context.TFBuild(), context.Globber, context.FileSystem, context.ProcessRunner, context.Tools);
        }

        [CakeMethodAlias]
        public static VersionResult GetNextVersionInfo(this ICakeContext context, string branch, string defaultVersion)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            var versionInfo = new VersionHelper(context.Environment, context.Log, context.Arguments, 
                                                context.AppVeyor(), context.TFBuild(), context.Globber, context.FileSystem, context.ProcessRunner, context.Tools)
            {
                Branch = branch
            };
            return versionInfo.GetNextVersion(defaultVersion);
        }
    }
}
