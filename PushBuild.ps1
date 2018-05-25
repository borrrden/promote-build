[CmdletBinding(DefaultParameterSetName='set2')]
param(
    [Parameter(ParameterSetName='set2', Mandatory=$true, HelpMessage="The version to download from S3")][string]$Version,
    [Parameter(ParameterSetName='set2', Mandatory=$true, HelpMessage="The access key of the AWS credentials")][string]$AccessKey,
    [Parameter(ParameterSetName='set2', Mandatory=$true, HelpMessage="The secret key of the AWS credentials")][string]$SecretKey,
    [Parameter(ParameterSetName='set1')][switch]$Prerelease,
    [Parameter(ParameterSetName='set2', Mandatory=$true, HelpMessage="The API key for pushing to the Nuget feed")]
    [Parameter(ParameterSetName='set1', Mandatory=$true, HelpMessage="The API key for pushing to the Nuget feed")][string]$NugetApiKey
)

  
if(-Not $Prerelease) {
    Write-Host "Prelease not specified, downloading packages from S3..."
    Read-S3Object -BucketName packages.couchbase.com -KeyPrefix releases/couchbase-lite/net/$Version -Folder . -AccessKey $AccessKey -SecretKey $SecretKey
    $NugetUrl = "https://api.nuget.org/v3/index.json"
} else {
    $NugetUrl = "http://mobile.nuget.couchbase.com/nuget/Developer"
}

if(![System.IO.File]::Exists("nuget.exe")) {
    Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile nuget.exe
}

foreach($file in (Get-ChildItem $pwd -Filter *.nupkg)) {
    if($Prerelease) {
        $NugetUrl = "http://mobile.nuget.couchbase.com/nuget/Developer"
    } else {
        $NugetUrl = "https://api.nuget.org/v3/index.json"
    }

    Write-Host "Pushing $file..."
    #& nuget.exe push $file -ApiKey $NugetApiKey -Source $NugetUrl
}