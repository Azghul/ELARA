#!/usr/bin/env bash
# Builds both Windows x64 releases and packages them as ZIPs:
#   1. Portable (self-contained; .NET runtime included)
#   2. Runtime-required (framework-dependent; needs Microsoft .NET 10 Desktop Runtime x64)
# Runs on Linux (requires the .NET SDK with EnableWindowsTargeting) and on Windows.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
version="$(grep -oP '(?<=<Version>)[^<]+' "${repo_root}/ELARA.csproj" | head -1)"
configuration="${CONFIGURATION:-Release}"
artifacts_dir="${repo_root}/artifacts"

license_files=(
    "LICENSE"
    "THIRD_PARTY_NOTICES.md"
    "LICENSE-LAME.txt"
    "LICENSE-NAudio.Core.txt"
    "LICENSE-NAudio.Lame.txt"
    "LICENSE-NETRuntime.txt"
    "LICENSE-NETRuntime-ThirdPartyNotices.txt"
    "LICENSE-DOTNET-LIBRARY.txt"
)

publish_variant() {
    # $1 = "true"/"false" (self-contained), $2 = output directory
    local self_contained="$1"
    local out_dir="$2"

    rm -rf "${out_dir}"
    mkdir -p "${out_dir}"

    dotnet publish "${repo_root}/ELARA.csproj" \
        -c "${configuration}" \
        -r win-x64 \
        --self-contained "${self_contained}" \
        --nologo \
        -o "${out_dir}"

    # Release hygiene: no debug symbols and no 32-bit LAME library in the x64-only ZIP.
    rm -f "${out_dir}"/*.pdb
    rm -f "${out_dir}/libmp3lame.32.dll"

    # Include license files with the distribution.
    for file in "${license_files[@]}"; do
        cp "${repo_root}/${file}" "${out_dir}/${file}"
    done

    # Include the ELARA application icon (used at runtime for the window/tray/About).
    mkdir -p "${out_dir}/assets"
    cp "${repo_root}/assets/elara.ico" "${out_dir}/assets/elara.ico"
}

validate_variant() {
    # $1 = output directory, $2 = variant label, $3 = "portable"/"runtime-required"
    local out_dir="$1"
    local label="$2"
    local variant="$3"
    local missing=0

    local required_files=(
        "ELARA.exe"
        "ELARA.dll"
        "NAudio.Lame.dll"
        "NAudio.Core.dll"
        "assets/elara.ico"
        "${license_files[@]}"
    )
    for file in "${required_files[@]}"; do
        if [[ -f "${out_dir}/${file}" ]]; then
            echo "OK      [${label}] ${file}"
        else
            echo "MISSING [${label}] ${file}"
            missing=1
        fi
    done

    # Native LAME x64 must be present, the x86 build must not.
    if [[ -f "${out_dir}/libmp3lame.64.dll" ]]; then
        echo "OK      [${label}] libmp3lame.64.dll"
    else
        echo "MISSING [${label}] libmp3lame.64.dll (required for win-x64)"
        missing=1
    fi
    if [[ -f "${out_dir}/libmp3lame.32.dll" ]]; then
        echo "FORBIDDEN PRESENT [${label}] libmp3lame.32.dll"
        missing=1
    else
        echo "OK      [${label}] libmp3lame.32.dll absent"
    fi
    if ls "${out_dir}"/*.pdb >/dev/null 2>&1; then
        echo "FORBIDDEN PRESENT [${label}] PDB files"
        missing=1
    else
        echo "OK      [${label}] no PDB files"
    fi

    # Never ship logs, settings, recordings or temporary PCM data.
    local forbidden_patterns=("*.log" "settings.json" "*.pcm" "*.tmp" "Recordings")
    for pattern in "${forbidden_patterns[@]}"; do
        if find "${out_dir}" -iname "${pattern}" -print -quit | grep -q .; then
            echo "FORBIDDEN PRESENT [${label}] ${pattern}"
            missing=1
        else
            echo "OK      [${label}] ${pattern} absent"
        fi
    done

    # Runtime expectations.
    if [[ "${variant}" == "portable" ]]; then
        # Self-contained: the .NET runtime must be embedded.
        local runtime_files=("coreclr.dll" "hostfxr.dll" "System.Private.CoreLib.dll")
        for file in "${runtime_files[@]}"; do
            if [[ -f "${out_dir}/${file}" ]]; then
                echo "OK      [${label}] ${file} (self-contained runtime)"
            else
                echo "MISSING [${label}] ${file} (self-contained runtime)"
                missing=1
            fi
        done
    else
        # Framework-dependent: the .NET runtime must NOT be embedded.
        for file in "coreclr.dll" "System.Private.CoreLib.dll"; do
            if [[ -f "${out_dir}/${file}" ]]; then
                echo "FORBIDDEN PRESENT [${label}] ${file} (runtime must stay external)"
                missing=1
            else
                echo "OK      [${label}] ${file} absent (runtime stays external)"
            fi
        done
    fi

    if [[ "${missing}" -ne 0 ]]; then
        echo
        echo "ERROR: publish output for ${label} is incomplete, aborting." >&2
        exit 1
    fi
}

make_zip() {
    # $1 = output directory, $2 = zip path
    local out_dir="$1"
    local zip_path="$2"

    rm -f "${zip_path}"
    if command -v zip >/dev/null 2>&1; then
        (cd "${out_dir}" && zip -q -r "${zip_path}" .)
    elif command -v python3 >/dev/null 2>&1; then
        python3 - "${out_dir}" "${zip_path}" <<'PY'
import os, sys, zipfile

out_dir, zip_path = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
    for root, _, files in os.walk(out_dir):
        for name in files:
            file_path = os.path.join(root, name)
            archive.write(file_path, os.path.relpath(file_path, out_dir))
PY
    else
        echo "ERROR: neither 'zip' nor 'python3' is available; the publish output is in ${out_dir} but no ZIP was created." >&2
        exit 1
    fi
}

portable_dir="${artifacts_dir}/publish-win-x64-portable"
runtime_dir="${artifacts_dir}/publish-win-x64-runtime-required"
portable_zip="${artifacts_dir}/ELARA-${version}-win-x64-portable.zip"
runtime_zip="${artifacts_dir}/ELARA-${version}-win-x64-runtime-required.zip"

echo "=== Publishing portable (self-contained) ==="
publish_variant "true" "${portable_dir}"
validate_variant "${portable_dir}" "portable" "portable"

echo
echo "=== Publishing runtime-required (framework-dependent) ==="
publish_variant "false" "${runtime_dir}"
validate_variant "${runtime_dir}" "runtime-required" "runtime-required"

make_zip "${portable_dir}" "${portable_zip}"
make_zip "${runtime_dir}" "${runtime_zip}"

echo
echo "=== Artifacts ==="
portable_bytes="$(stat -c %s "${portable_zip}" 2>/dev/null || stat -f %z "${portable_zip}")"
runtime_bytes="$(stat -c %s "${runtime_zip}" 2>/dev/null || stat -f %z "${runtime_zip}")"
portable_mb="$(python3 -c "print(f'{$portable_bytes / 1048576:.1f}')")"
runtime_mb="$(python3 -c "print(f'{$runtime_bytes / 1048576:.1f}')")"
saved_bytes=$((portable_bytes - runtime_bytes))
saved_percent="$(python3 -c "print(f'{($saved_bytes / $portable_bytes) * 100:.1f}')")"
echo "Portable ZIP:         ${portable_zip} (${portable_mb} MB)"
echo "Runtime-required ZIP: ${runtime_zip} (${runtime_mb} MB)"
echo "Savings:              ${saved_bytes} bytes (${saved_percent}% smaller than portable)"
echo
echo "SHA256 portable:         $(sha256sum "${portable_zip}" | cut -d' ' -f1)"
echo "SHA256 runtime-required: $(sha256sum "${runtime_zip}" | cut -d' ' -f1)"
echo
echo "Created:"
echo "  ${portable_zip}"
echo "  ${runtime_zip}"