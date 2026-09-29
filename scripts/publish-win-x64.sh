#!/usr/bin/env bash
# Builds a self-contained Windows x64 release and packages it as a ZIP.
# Runs on Linux (requires the .NET SDK with EnableWindowsTargeting) and on Windows.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
version="$(grep -oP '(?<=<Version>)[^<]+' "${repo_root}/SimpleAudioRecorder.csproj" | head -1)"
configuration="${CONFIGURATION:-Release}"
output_dir="${repo_root}/artifacts"
publish_dir="${output_dir}/publish-win-x64"
zip_path="${output_dir}/ELARA-${version}-win-x64.zip"

rm -rf "${publish_dir}"
mkdir -p "${output_dir}"

dotnet publish "${repo_root}/SimpleAudioRecorder.csproj" \
    -c "${configuration}" \
    -r win-x64 \
    --self-contained \
    -o "${publish_dir}"

# Release hygiene: no debug symbols and no 32-bit LAME library in the x64-only ZIP.
rm -f "${publish_dir}"/*.pdb
rm -f "${publish_dir}/libmp3lame.32.dll"

# Include license files with the distribution.
cp "${repo_root}/LICENSE" "${publish_dir}/LICENSE"
cp "${repo_root}/THIRD_PARTY_NOTICES.md" "${publish_dir}/THIRD_PARTY_NOTICES.md"

echo
echo "=== Publish output (${publish_dir}) ==="
ls -la "${publish_dir}"

echo
echo "=== Required files check ==="
required_files=(
    "SimpleAudioRecorder.exe"
    "SimpleAudioRecorder.dll"
    "NAudio.Lame.dll"
    "NAudio.Core.dll"
    "LICENSE"
    "THIRD_PARTY_NOTICES.md"
)
missing=0
for file in "${required_files[@]}"; do
    if [[ -f "${publish_dir}/${file}" ]]; then
        echo "OK      ${file}"
    else
        echo "MISSING ${file}"
        missing=1
    fi
done

# The NAudio.Lame package ships the native LAME encoder as libmp3lame.32.dll /
# libmp3lame.64.dll (no assumptions beyond that: list whatever native files exist).
native_files=("${publish_dir}"/libmp3lame.*.dll)
if [[ -e "${native_files[0]}" ]]; then
    for file in "${native_files[@]}"; do
        echo "OK      $(basename "${file}")"
    done
else
    echo "MISSING libmp3lame.*.dll (native LAME encoder)"
    missing=1
fi

if [[ ! -f "${publish_dir}/libmp3lame.64.dll" ]]; then
    echo "MISSING libmp3lame.64.dll (required for win-x64)"
    missing=1
fi

forbidden_files=(
    "SimpleAudioRecorder.pdb"
    "libmp3lame.32.dll"
)
for file in "${forbidden_files[@]}"; do
    if [[ -f "${publish_dir}/${file}" ]]; then
        echo "FORBIDDEN PRESENT ${file}"
        missing=1
    else
        echo "OK      ${file} absent"
    fi
done

if [[ "${missing}" -ne 0 ]]; then
    echo
    echo "ERROR: publish output is incomplete, aborting." >&2
    exit 1
fi

rm -f "${zip_path}"
if command -v zip >/dev/null 2>&1; then
    (cd "${publish_dir}" && zip -q -r "${zip_path}" .)
elif command -v python3 >/dev/null 2>&1; then
    python3 - "${publish_dir}" "${zip_path}" <<'PY'
import os, sys, zipfile

publish_dir, zip_path = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
    for root, _, files in os.walk(publish_dir):
        for name in files:
            file_path = os.path.join(root, name)
            archive.write(file_path, os.path.relpath(file_path, publish_dir))
PY
else
    echo "ERROR: neither 'zip' nor 'python3' is available; the publish output is in ${publish_dir} but no ZIP was created." >&2
    exit 1
fi

echo
echo "=== ZIP contents ==="
if command -v unzip >/dev/null 2>&1; then
    unzip -l "${zip_path}" | head -30
else
    python3 - "${zip_path}" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    infos = archive.infolist()
    for info in infos[:30]:
        print(f"{info.file_size:>12}  {info.filename}")
    print(f"... {len(infos)} files total")
PY
fi
echo
echo "Created ${zip_path}"