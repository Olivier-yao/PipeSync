while ($true) {

    git add .

    $changes = git diff --cached --name-only

    if ($changes) {

        $date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

        git commit -m "AutoSync $date"

        if ($LASTEXITCODE -eq 0) {

            git push origin main

            if ($LASTEXITCODE -eq 0) {
                Write-Host "[$date] Synchronisation réussie"
            }
        }
    }

    Start-Sleep 5
}