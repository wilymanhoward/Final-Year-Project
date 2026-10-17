# Placeholder voice for the briefing detective: Windows' built-in offline text-to-speech (System.Speech, no installs,
# nothing sent online). Reads Assets/_Project/Audio/Briefing/briefing_lines.csv and writes <file>.wav next to it.
# Only creates clips that don't exist yet (any extension: .wav/.mp3/.ogg), so it never overwrites the real AI voice
# files - drop those in with the same base names (e.g. intro_01.mp3). -Force regenerates all .wav placeholders.
#
# Run from the repo root (Windows PowerShell):
#   powershell -ExecutionPolicy Bypass -File tools/make_briefing_tts.ps1            (add -Force to redo all)
#   optional: -Voice "Microsoft David Desktop"   (list: see the output of the first run)
param(
    [switch]$Force,
    [string]$Voice = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Speech

$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root "Assets/_Project/Audio/Briefing"
$csv = Join-Path $folder "briefing_lines.csv"
if (-not (Test-Path $csv)) { throw "Missing $csv" }

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$installed = $synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name }
Write-Host ("Installed voices: " + ($installed -join ", "))
if ($Voice -ne "") {
    $synth.SelectVoice($Voice)
} else {
    $male = $installed | Where-Object { $_ -match "David|Mark|George|James" } | Select-Object -First 1
    if ($male) { $synth.SelectVoice($male) }
}
Write-Host ("Using voice: " + $synth.Voice.Name)
$synth.Rate = -1   # a little slower than default: clearer in a headset

$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$made = 0; $kept = 0
foreach ($row in Import-Csv $csv) {
    $name = $row.file
    if ($name -notmatch '^[A-Za-z0-9_]+$') { Write-Warning "Skipping unsafe file name '$name'"; continue }
    $existing = Get-ChildItem -Path $folder -File | Where-Object { $_.BaseName -eq $name -and $_.Extension -in ".wav", ".mp3", ".ogg" }
    $wav = Join-Path $folder ($name + ".wav")
    if ($existing -and -not ($Force -and $existing.Extension -eq ".wav" -and @($existing).Count -eq 1)) { $kept++; continue }
    $synth.SetOutputToWaveFile($wav, $format)
    $synth.Speak($row.text)
    $synth.SetOutputToNull()
    $made++
    Write-Host "  $name.wav"
}
$synth.Dispose()
Write-Host "Done: $made clip(s) generated, $kept kept (already there)."
