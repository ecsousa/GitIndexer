# All rights reserved. This program and the accompanying materials
# are made available under the terms of the GNU Lesser General Public License
# (LGPL) version 2.1 which accompanies this distribution, and is available at
# http://www.gnu.org/licenses/lgpl-2.1.html
#
# This library is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
# Lesser General Public License for more details.

param($installPath, $toolsPath, $package, $project)

$errorCondition = ' ''$(GitIndexer)'' != ''true'' '

# Need to load MSBuild assembly if it's not loaded yet.
Add-Type -AssemblyName 'Microsoft.Build, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'

# Grab the loaded MSBuild project for the project
$msbuild = [Microsoft.Build.Evaluation.ProjectCollection]::GlobalProjectCollection.GetLoadedProjects($project.FullName) | Select-Object -First 1

$target = $msbuild.Xml.Targets | ? { $_.Name -eq 'BeforeBuild' } | Select-Object -First 1;

if($target) {
    $error = $target.Tasks | ? { ($_.Name -eq 'Error') -and ($_.Condition -eq $errorCondition) } | Select-Object -First 1;

    if($error) {
        $target.RemoveChild($error);
    }
}

