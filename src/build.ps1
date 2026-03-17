param (
	[Parameter()]
	[ValidateNotNullOrEmpty()]
	[string]
	$OutputPath = '.\bin\WSTV'
)

if ( Test-Path -Path .\bin\WSTV) {
    rm -Recurse -Force $OutputPath
}

Write-Host 'Building'

dotnet publish `
	WSTV.csproj `
	-c Release `
	--self-contained false `
	-o $OutputPath

if ( -Not $? ) {
	exit $lastExitCode
	}

if ( Test-Path -Path .\bin\WSTV) {
    rm -Force "$OutputPath\*.pdb"
    rm -Force "$OutputPath\*.xml"
}

Write-Host 'Build done'

7z a .\bin\WSTV.zip $OutputPath

ls $OutputPath



exit 0