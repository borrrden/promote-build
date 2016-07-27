//
// RelativeAssemblyResolver.cs
//
// Author:
// 	Jim Borden  <jim.borden@couchbase.com>
//
// Copyright (c) 2016 Couchbase, Inc All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace PromoteBuild
{
    public class RelativeAssemblyResolver : DefaultAssemblyResolver
    {
        private readonly string _basePath;
        private string _platform;

        private Dictionary<AssemblyNameReference, AssemblyDefinition> _nameToPath = new Dictionary<AssemblyNameReference, AssemblyDefinition>();

        public RelativeAssemblyResolver(string basePath, string platform)
        {
            _basePath = basePath;
            _platform = platform;
        }

        private AssemblyDefinition GetDefinition(AssemblyNameReference name)
        {
            

            if (_nameToPath.ContainsKey(name))
            {
                return _nameToPath[name];
            }

            if (name.Name == "Xamarin.iOS")
            {
                _nameToPath[name] = AssemblyDefinition.ReadAssembly("/Library/Frameworks/Xamarin.iOS.framework/Versions/Current/lib/64bits/Xamarin.iOS.dll");
                return _nameToPath[name];
            }

            var d = Directory.EnumerateDirectories(_basePath, $"{name.Name}*").FirstOrDefault();
            if (d == null)
            {
                throw new AssemblyResolutionException(name);
            }

            _nameToPath[name] = AssemblyDefinition.ReadAssembly(Path.Combine(d, "lib", _platform, $"{name.Name}.dll"));
            return _nameToPath[name];
        }

        public override AssemblyDefinition Resolve(string fullName)
        {
            return base.Resolve(fullName);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            try
            {
                return base.Resolve(name);
            }
            catch (AssemblyResolutionException)
            {
                if (name.Name != "Couchbase.Lite.Listener" && _platform == "xamarinios10")
                {
                    _platform = "Xamarin.iOS10";
                }

                return GetDefinition(name);
            }
        }

        public override AssemblyDefinition Resolve(string fullName, ReaderParameters parameters)
        {
            return base.Resolve(fullName, parameters);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            try
            {
                return base.Resolve(name, parameters);
            }
            catch (AssemblyResolutionException)
            {
                if (name.Name != "Couchbase.Lite.Listener" && _platform == "xamarinios10")
                {
                    _platform = "Xamarin.iOS10";
                }

                return GetDefinition(name);
            }
        }
    }
}

