<#
.SYNOPSIS
    Automatizovana test skripta za YoutubeSentimentServer projekat.

.DESCRIPTION
    Testira sve glavne HTTP endpoint-e, rubne slucajeve, tacnost sentiment analize
    i konkurentnost, direktno preko HTTP-a (kao spoljasnji klijent). Ne zahteva
    build alata niti izmenu koda - server samo mora vec da radi (F5 u VS-u ili
    "dotnet run").

.PARAMETER BaseUrl
    Osnovna adresa servera. Podrazumevano http://localhost:5038

.PARAMETER VideoId
    YouTube video ID koji se koristi za testiranje registracije/komentara/sentimenta.
    Preporucuje se video sa vise desetina komentara.

.PARAMETER RunShutdownTest
    Ako je prisutno, na kraju testira i /shutdown endpoint - OVO CE UGASITI SERVER.
    Podrazumevano se preskace, koristite ga tek kad ste zavrsili sa svim ostalim testiranjem.

.EXAMPLE
    .\test-projekat.ps1

.EXAMPLE
    .\test-projekat.ps1 -VideoId "dQw4w9WgXcQ" -BaseUrl "http://localhost:5038"

.EXAMPLE
    .\test-projekat.ps1 -RunShutdownTest
#>

param(
    [string]$BaseUrl = "http://localhost:5038",
    [string]$VideoId = "li9qaVT8Kiw",
    [switch]$RunShutdownTest
)

$script:PassCount = 0
$script:FailCount = 0
$script:SkipCount = 0

function Write-TestHeader {
    param([string]$Text)
    Write-Host ""
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Write-Pass {
    param([string]$Text)
    Write-Host "  [PASS] $Text" -ForegroundColor Green
    $script:PassCount++
}

function Write-Fail {
    param([string]$Text)
    Write-Host "  [FAIL] $Text" -ForegroundColor Red
    $script:FailCount++
}

function Write-SkipMsg {
    param([string]$Text)
    Write-Host "  [SKIP] $Text" -ForegroundColor Yellow
    $script:SkipCount++
}

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null
    )

    $uri = "$BaseUrl$Path"
    $callParams = @{
        Method      = $Method
        Uri         = $uri
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $callParams["Body"] = ($Body | ConvertTo-Json)
        $callParams["ContentType"] = "application/json"
    }

    try {
        $response = Invoke-RestMethod @callParams
        return @{ Success = $true; StatusCode = 200; Data = $response }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
        }
        return @{ Success = $false; StatusCode = $statusCode; Error = $_.Exception.Message }
    }
}

Write-Host "###########################################################"
Write-Host "#  YoutubeSentimentServer - automatizovano testiranje"
Write-Host "#  Server:        $BaseUrl"
Write-Host "#  Video za test: $VideoId"
Write-Host "###########################################################"

# -----------------------------------------------------------------
Write-TestHeader "0. Da li server uopste odgovara"
# -----------------------------------------------------------------

$ping = Invoke-Api -Method GET -Path "/health"
if (-not $ping.Success) {
    Write-Host ""
    Write-Host "Server ne odgovara na $BaseUrl - proverite da li je pokrenut, i da li je port tacan." -ForegroundColor Red
    Write-Host "Greska: $($ping.Error)" -ForegroundColor Red
    exit 1
}
Write-Pass "Server odgovara na $BaseUrl"

# -----------------------------------------------------------------
Write-TestHeader "1. Osnovne provere (health, akka, config)"
# -----------------------------------------------------------------

$health = Invoke-Api -Method GET -Path "/health"
if ($health.Success -and $health.Data.status -eq "Healthy") {
    Write-Pass "/health vraca status Healthy"
} else {
    Write-Fail "/health nije vratio ocekivan odgovor"
}

$healthAkka = Invoke-Api -Method GET -Path "/health/akka"
if ($healthAkka.Success -and $healthAkka.Data.status -eq "Akka running") {
    Write-Pass "/health/akka potvrdjuje da je Akka ActorSystem pokrenut"
} else {
    Write-Fail "/health/akka nije vratio ocekivan odgovor"
}

$config = Invoke-Api -Method GET -Path "/config/youtube"
if ($config.Success) {
    Write-Pass "/config/youtube dostupan"
    if ($config.Data.apiKeyConfigured -eq $true) {
        Write-Pass "YouTube API kljuc je podesen"
    } else {
        Write-Fail "YouTube API kljuc NIJE podesen - Rx.NET tok nece moci da poziva pravi YouTube API"
    }
} else {
    Write-Fail "/config/youtube nije dostupan"
}

# -----------------------------------------------------------------
Write-TestHeader "2. Registracija videa i idempotentnost"
# -----------------------------------------------------------------

$register1 = Invoke-Api -Method POST -Path "/videos/$VideoId"
if ($register1.Success -and $register1.Data.data.registeredNow -eq $true) {
    Write-Pass "Prva registracija videa '$VideoId' uspesna (registeredNow: true)"
} else {
    Write-Fail "Prva registracija nije uspela kako treba"
}

$register2 = Invoke-Api -Method POST -Path "/videos/$VideoId"
if ($register2.Success -and $register2.Data.data.registeredNow -eq $false) {
    Write-Pass "Ponovljena registracija ispravno prepoznata kao duplikat (registeredNow: false)"
} else {
    Write-Fail "Ponovljena registracija nije ispravno obradjena"
}

$videoList = Invoke-Api -Method GET -Path "/videos"
$videoIdsInList = @()
if ($videoList.Success) { $videoIdsInList = $videoList.Data.data | ForEach-Object { $_.videoId } }

if ($videoIdsInList -contains $VideoId) {
    Write-Pass "GET /videos sadrzi registrovan video '$VideoId'"
} else {
    Write-Fail "GET /videos ne sadrzi ocekivan video"
}

# -----------------------------------------------------------------
Write-TestHeader "3. Rubni slucajevi - neregistrovan video"
# -----------------------------------------------------------------

$fakeVideoId = "NEPOSTOJECI_TEST_ID_$(Get-Random)"

$fakeSentiment = Invoke-Api -Method GET -Path "/videos/$fakeVideoId/sentiment"
if ($fakeSentiment.Success -and $fakeSentiment.Data.data.totalComments -eq 0) {
    Write-Pass "Sentiment za neregistrovan video vraca prazan rezultat (totalComments: 0), bez greske"
} else {
    Write-Fail "Neregistrovan video - sentiment se ne ponasa kako treba"
}

$fakeComments = Invoke-Api -Method GET -Path "/videos/$fakeVideoId/comments"
if ($fakeComments.Success -and $fakeComments.Data.data.Count -eq 0) {
    Write-Pass "Komentari za neregistrovan video vracaju praznu listu, bez greske"
} else {
    Write-Fail "Neregistrovan video - komentari se ne ponasaju kako treba"
}

# -----------------------------------------------------------------
Write-TestHeader "4. Validacija ulaza"
# -----------------------------------------------------------------

$spaceId = [Uri]::EscapeDataString("   ")
$invalidVideoId = Invoke-Api -Method POST -Path "/videos/$spaceId"
if ($invalidVideoId.Success -and $invalidVideoId.Data.data.registeredNow -eq $false) {
    Write-Pass "Video ID koji je samo razmaci ispravno odbijen (bez pucanja servera)"
} else {
    Write-Fail "Video ID sa samo razmacima nije ispravno obradjen: $($invalidVideoId.Error)"
}

# -----------------------------------------------------------------
Write-TestHeader "5. Manuelni test-komentar i tacnost sentimenta (samo Development)"
# -----------------------------------------------------------------

$emptyCommentTest = Invoke-Api -Method POST -Path "/videos/$VideoId/comments/test" -Body @{ author = "Test"; text = "" }

if ($emptyCommentTest.StatusCode -eq 404) {
    Write-SkipMsg "/comments/test ne postoji (server nije u Development okruzenju) - preskacem sekciju 5"
}
else {
    if ($emptyCommentTest.StatusCode -eq 400) {
        Write-Pass "Prazan tekst komentara ispravno odbijen sa 400 Bad Request"
    } else {
        Write-Fail "Prazan tekst komentara nije odbijen kako treba (status: $($emptyCommentTest.StatusCode))"
    }

    $sentimentBefore = Invoke-Api -Method GET -Path "/videos/$VideoId/sentiment"
    $positiveBefore = $sentimentBefore.Data.data.positiveCount
    $negativeBefore = $sentimentBefore.Data.data.negativeCount

    $posComment = Invoke-Api -Method POST -Path "/videos/$VideoId/comments/test" -Body @{
        author = "Test Pozitivan"
        text   = "This is absolutely wonderful, I love it, amazing content, best video ever!"
    }
    if ($posComment.Success -and $posComment.Data.data.added -eq $true) {
        Write-Pass "Ocigledno pozitivan test-komentar uspesno dodat"
    } else {
        Write-Fail "Pozitivan test-komentar nije dodat"
    }

    $negComment = Invoke-Api -Method POST -Path "/videos/$VideoId/comments/test" -Body @{
        author = "Test Negativan"
        text   = "This is terrible, I hate this, worst video ever, awful and disgusting"
    }
    if ($negComment.Success -and $negComment.Data.data.added -eq $true) {
        Write-Pass "Ocigledno negativan test-komentar uspesno dodat"
    } else {
        Write-Fail "Negativan test-komentar nije dodat"
    }

    Start-Sleep -Seconds 1
    $sentimentAfter = Invoke-Api -Method GET -Path "/videos/$VideoId/sentiment"
    $positiveAfter = $sentimentAfter.Data.data.positiveCount
    $negativeAfter = $sentimentAfter.Data.data.negativeCount

    if ($positiveAfter -gt $positiveBefore) {
        Write-Pass "positiveCount se povecao posle pozitivnog komentara ($positiveBefore -> $positiveAfter)"
    } else {
        Write-Fail "positiveCount se NIJE povecao posle pozitivnog komentara ($positiveBefore -> $positiveAfter)"
    }

    if ($negativeAfter -gt $negativeBefore) {
        Write-Pass "negativeCount se povecao posle negativnog komentara ($negativeBefore -> $negativeAfter)"
    } else {
        Write-Fail "negativeCount se NIJE povecao posle negativnog komentara ($negativeBefore -> $negativeAfter) - proverite ML.NET Probability fix!"
    }
}

# -----------------------------------------------------------------
Write-TestHeader "6. Konkurentnost - paralelna registracija vise videa"
# -----------------------------------------------------------------
Write-Host "  (koristi Start-Job, pa moze potrajati par sekundi due to process startup overhead)" -ForegroundColor DarkGray

$parallelVideoIds = 1..5 | ForEach-Object { "PARALLEL_TEST_${_}_$(Get-Random)" }

$jobs = foreach ($id in $parallelVideoIds) {
    Start-Job -ScriptBlock {
        param($url, $vid)
        try {
            Invoke-RestMethod -Method Post -Uri "$url/videos/$vid" -ErrorAction Stop | Out-Null
            return @{ VideoId = $vid; Success = $true }
        }
        catch {
            return @{ VideoId = $vid; Success = $false; Error = $_.Exception.Message }
        }
    } -ArgumentList $BaseUrl, $id
}

$jobResults = $jobs | Wait-Job | Receive-Job
$jobs | Remove-Job

$successCount = ($jobResults | Where-Object { $_.Success }).Count

if ($successCount -eq $parallelVideoIds.Count) {
    Write-Pass "Svih $($parallelVideoIds.Count) paralelnih registracija uspesno (nema race condition-a)"
} else {
    Write-Fail "Samo $successCount od $($parallelVideoIds.Count) paralelnih registracija uspesno"
}

$videoListAfterParallel = Invoke-Api -Method GET -Path "/videos"
$currentVideoIds = @()
if ($videoListAfterParallel.Success) {
    $currentVideoIds = $videoListAfterParallel.Data.data | ForEach-Object { $_.videoId }
}

$foundCount = 0
foreach ($id in $parallelVideoIds) {
    if ($currentVideoIds -contains $id) { $foundCount++ }
}

if ($foundCount -eq $parallelVideoIds.Count) {
    Write-Pass "Svi paralelno registrovani video ID-jevi se pojavljuju u GET /videos"
} else {
    Write-Fail "Samo $foundCount od $($parallelVideoIds.Count) paralelno registrovanih videa se pojavljuje u listi"
}

# -----------------------------------------------------------------
if ($RunShutdownTest) {
    Write-TestHeader "7. Graceful shutdown (SERVER CE SE UGASITI POSLE OVOGA)"

    $shutdown = Invoke-Api -Method POST -Path "/shutdown"
    if ($shutdown.Success) {
        Write-Pass "Zahtev za gasenje prihvacen, server bi trebalo da se gasi"
    }
    elseif ($shutdown.StatusCode -eq 404) {
        Write-SkipMsg "/shutdown ne postoji (server nije u Development okruzenju)"
    }
    else {
        Write-Fail "Neocekivan odgovor na /shutdown: $($shutdown.Error)"
    }
}
else {
    Write-TestHeader "7. Graceful shutdown"
    Write-SkipMsg "Preskoceno (pokrenite skriptu sa -RunShutdownTest da ga testirate - OVO GASI SERVER)"
}

# -----------------------------------------------------------------
Write-Host ""
Write-Host "###########################################################"
Write-Host "#  REZULTATI: $($script:PassCount) PASS / $($script:FailCount) FAIL / $($script:SkipCount) SKIP"
Write-Host "###########################################################"

if ($script:FailCount -gt 0) {
    Write-Host "Ima neuspesnih testova - pogledajte [FAIL] linije iznad." -ForegroundColor Red
    exit 1
}
else {
    Write-Host "Svi izvrseni testovi su prosli!" -ForegroundColor Green
    exit 0
}
