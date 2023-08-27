$branch = $env:SOURCE_BRANCH_NAME
$buildNumber = $env:BUILD_NUMBER
$packageVersion = "$buildNumber"
if ( $branch -eq 'master' )
{
	$parts = $buildNumber -Split '-'
	$packageVersion = $parts[0]
}
Write-Host "##vso[task.setvariable variable=messagesPackageVersion]$packageVersion"