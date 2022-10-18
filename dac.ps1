foreach ($file in (Get-ChildItem C:\github\dbatools-library\bin\sqlpackage\windows-core)) {
    if ($file.Name -match "sqlpackage" -or $file.Name -match "System.Collections.Concurrent" -or $file -match "trace") {
        continue
    }

    $file | Write-Warning
    Remove-Item $file -ErrorAction Ignore
    $output = .\sqlpackage.exe | Out-String
    if ($output -notmatch "Specifies a name value pair for an action-specific variable") {
        Copy-Item "C:\github\dbatools-library\temp\windows\$($file.Name)" -Destination C:\github\dbatools-library\bin\sqlpackage\windows-core -Verbose
    }
}

foreach ($file in (Get-ChildItem /mnt/c/github/dbatools-library/bin/sqlpackage/linux)) {
    if ($file.Name -match "sqlpackage" -or $file.Name -match "System.Collections.Concurrent" -or $file -match "trace" -or $file.Name -match "json") {
        continue
    }

    #$file | Write-Warning
    Remove-Item $file -ErrorAction Ignore
    $output = .\sqlpackage | Out-String
    if ($output -notmatch "Specifies a name value pair for an") {
        $output
        Copy-Item "/mnt/c/github/dbatools-library/temp/linux/$($file.Name)" -Destination /mnt/c/github/dbatools-library/bin/sqlpackage/linux -Verbose -ErrorAction Stop
    }
}



### MacOS

foreach ($file in (Get-ChildItem $home/Desktop/dbatools-library/bin/sqlpackage/mac)) {
    if ($file.Name -match "sqlpackage" -or $file.Name -match "System.Collections.Concurrent" -or $file -match "trace" -or $file.Name -match "json") {
        continue
    }

    #$file | Write-Warning
    Remove-Item $file -ErrorAction Ignore
    $output = ./sqlpackage | Out-String
    if ($output -notmatch "Specifies a name value pair for an") {
        $output
        Copy-Item "$home/Desktop/dbatools-library/temp/macos/$($file.Name)" -Destination $home/Desktop/dbatools-library/bin/sqlpackage/mac -Verbose -ErrorAction Stop
    }
}

