// SPDX-License-Identifier: LGPL-2.1
// Copyright 2024 Fujifier Project / Isidore Paulin

use std::ffi::CStr;
use std::fs::File;
use std::io::BufWriter;
use std::os::raw::c_char;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::PathBuf;
use std::sync::Once;
use rawler::dng::DngCompression;
use rawler::dng::convert::{ConvertParams, convert_raw_file};
use rawler::decoders::supported_extensions;

#[cfg(not(target_os = "ios"))]
#[global_allocator]
static GLOBAL: mimalloc::MiMalloc = mimalloc::MiMalloc;

static INIT_HOOK: Once = Once::new();

fn ensure_panic_hook_installed() {
    INIT_HOOK.call_once(|| {
        std::panic::set_hook(Box::new(|_info| {
            // Suppress default multi-thread stderr dumping.
            // The panic message is safely caught by catch_unwind and returned in error_msg_buf.
        }));
    });
}

/// Returns the library version string.
#[unsafe(no_mangle)]
pub extern "C" fn dnglab_version() -> *const c_char {
    c"0.8.0-ffi".as_ptr()
}

/// Checks if an extension (e.g. "raf", "arw", "cr2") is supported by the rawler engine.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_is_extension_supported(ext_ptr: *const c_char) -> bool {
    ensure_panic_hook_installed();
    let result = catch_unwind(AssertUnwindSafe(|| {
        if ext_ptr.is_null() {
            return false;
        }
        let ext_str = match unsafe { CStr::from_ptr(ext_ptr) }.to_str() {
            Ok(s) => s.trim_start_matches('.').to_lowercase(),
            Err(_) => return false,
        };

        let supported = supported_extensions();
        supported.iter().any(|&e| e.eq_ignore_ascii_case(&ext_str))
    }));

    result.unwrap_or(false)
}

/// Converts a RAW camera file to DNG using lossless compression.
///
/// Returns:
///   0 on success
///   Non-zero on error (and populates error_msg_buf with a null-terminated message if provided)
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_convert_file(
    input_path_ptr: *const c_char,
    output_path_ptr: *const c_char,
    embed_raw: bool,
    error_msg_buf: *mut c_char,
    error_msg_buf_len: usize,
) -> i32 {
    unsafe {
        dnglab_convert_file_with_options(
            input_path_ptr,
            output_path_ptr,
            embed_raw,
            true,
            error_msg_buf,
            error_msg_buf_len,
        )
    }
}

/// Converts a RAW camera file to DNG with explicit compression selection.
///
/// Returns:
///   0 on success
///   Non-zero on error (and populates error_msg_buf with a null-terminated message if provided)
#[unsafe(no_mangle)]
pub unsafe extern "C" fn dnglab_convert_file_with_options(
    input_path_ptr: *const c_char,
    output_path_ptr: *const c_char,
    embed_raw: bool,
    lossless_compression: bool,
    error_msg_buf: *mut c_char,
    error_msg_buf_len: usize,
) -> i32 {
    ensure_panic_hook_installed();

    let result = catch_unwind(AssertUnwindSafe(|| {
        if input_path_ptr.is_null() || output_path_ptr.is_null() {
            set_error(error_msg_buf, error_msg_buf_len, "Null pointer passed for input or output path");
            return -1;
        }

        let input_str = match unsafe { CStr::from_ptr(input_path_ptr) }.to_str() {
            Ok(s) => s,
            Err(_) => {
                set_error(error_msg_buf, error_msg_buf_len, "Input path is not valid UTF-8");
                return -2;
            }
        };

        let output_str = match unsafe { CStr::from_ptr(output_path_ptr) }.to_str() {
            Ok(s) => s,
            Err(_) => {
                set_error(error_msg_buf, error_msg_buf_len, "Output path is not valid UTF-8");
                return -3;
            }
        };

        let in_path = PathBuf::from(input_str);
        let out_path = PathBuf::from(output_str);

        if !in_path.exists() {
            set_error(error_msg_buf, error_msg_buf_len, &format!("Input file does not exist: {}", in_path.display()));
            return -4;
        }

        if out_path.exists() {
            if let Err(e) = std::fs::remove_file(&out_path) {
                set_error(error_msg_buf, error_msg_buf_len, &format!("Cannot overwrite existing file {}: {e}", out_path.display()));
                return -5;
            }
        }

        let file = match File::create(&out_path) {
            Ok(f) => f,
            Err(e) => {
                set_error(error_msg_buf, error_msg_buf_len, &format!("Failed to create destination file {}: {e}", out_path.display()));
                return -6;
            }
        };
        let mut writer = BufWriter::new(file);

        let mut params = ConvertParams::default();
        params.embedded = embed_raw;
        params.compression = if lossless_compression {
            DngCompression::Lossless
        } else {
            DngCompression::Uncompressed
        };
        params.software = "Fujifier / DNGLab Native".into();

        match convert_raw_file(&in_path, &mut writer, &params) {
            Ok(_) => 0,
            Err(e) => {
                let err_str = e.to_string();
                if err_str.contains("Unknown camera") || err_str.contains("Couldn't find a decoder") {
                    drop(writer);
                    let _ = std::fs::remove_file(&out_path);
                    if let Some(norm) = try_create_temp_with_normalized_camera(&in_path, &err_str) {
                        let retry_res = (|| -> Result<(), String> {
                            let file = File::create(&out_path).map_err(|e| e.to_string())?;
                            let mut retry_writer = BufWriter::new(file);
                            convert_raw_file(&norm.path, &mut retry_writer, &params).map_err(|e| e.to_string())?;
                            Ok(())
                        })();
                        let _ = std::fs::remove_file(&norm.path);
                        if retry_res.is_ok() {
                            if norm.is_5d_minolta {
                                restore_minolta_5d_model(&out_path);
                            }
                            return 0;
                        }
                    }
                } else {
                    drop(writer);
                    let _ = std::fs::remove_file(&out_path);
                }

                if err_str.contains("HighEfficency") {
                    set_error(error_msg_buf, error_msg_buf_len, "Nikon High Efficiency (HE/HE*) raw compression uses the proprietary intoPIX TicoRAW codec. Open-source decoders cannot decode it. Please set your camera to 'Lossless compression' RAW.");
                    return -7;
                }

                if err_str.contains("arw6") {
                    set_error(error_msg_buf, error_msg_buf_len, "Sony ARW6 wavelet compression is an experimental format not supported by open-source decoders. Standard Sony Compressed and Lossless Compressed RAW modes are fully supported.");
                    return -7;
                }

                if err_str.contains("PowerShot Pro70") || err_str.contains("PowerShot Pr070") {
                    set_error(error_msg_buf, error_msg_buf_len, "Canon PowerShot Pro70 uses a legacy non-Bayer CYGM sensor which is unsupported for DNG conversion.");
                    return -7;
                }

                set_error(error_msg_buf, error_msg_buf_len, &format!("Raw decode failed: {e}"));
                -7
            }
        }
    }));

    match result {
        Ok(code) => code,
        Err(panic_err) => {
            // Remove incomplete output file on panic
            if !output_path_ptr.is_null() {
                if let Ok(s) = unsafe { CStr::from_ptr(output_path_ptr) }.to_str() {
                    let _ = std::fs::remove_file(s);
                }
            }

            let msg = if let Some(s) = panic_err.downcast_ref::<&str>() {
                format!("Raw decode failed (decoder panic): {s}")
            } else if let Some(s) = panic_err.downcast_ref::<String>() {
                format!("Raw decode failed (decoder panic): {s}")
            } else {
                "Raw decode failed (internal decoder panic)".to_string()
            };

            let is_pro70 = if !input_path_ptr.is_null() {
                if let Ok(s) = unsafe { CStr::from_ptr(input_path_ptr) }.to_str() {
                    let p = std::path::Path::new(s);
                    p.to_string_lossy().to_ascii_lowercase().contains("pro70")
                        || std::fs::read(p).map_or(false, |b| {
                            b.windows(15).any(|w| w == b"PowerShot Pro70" || w == b"PowerShot Pr070")
                        })
                } else {
                    false
                }
            } else {
                false
            };

            if is_pro70 || msg.contains("PowerShot Pro70") || msg.contains("PowerShot Pr070") {
                set_error(
                    error_msg_buf,
                    error_msg_buf_len,
                    "Canon PowerShot Pro70 uses a legacy non-Bayer CYGM sensor which is unsupported for DNG conversion.",
                );
            } else {
                set_error(error_msg_buf, error_msg_buf_len, &msg);
            }
            -99
        }
    }
}

fn set_error(buf: *mut c_char, len: usize, msg: &str) {
    if buf.is_null() || len == 0 {
        return;
    }
    let bytes = msg.as_bytes();
    let copy_len = bytes.len().min(len - 1);
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr() as *const c_char, buf, copy_len);
        *buf.add(copy_len) = 0;
    }
}

struct NormalizedCamera {
    path: PathBuf,
    is_5d_minolta: bool,
}

fn replace_bytes_in_place(data: &mut [u8], err_str: &str, err_hint: Option<&str>, target: &[u8], replacement: &[u8]) -> bool {
    if target.is_empty() || replacement.len() != target.len() || data.len() < target.len() {
        return false;
    }
    let hint_matches = err_hint.map_or(false, |h| err_str.contains(h));
    if !hint_matches && !data.windows(target.len()).any(|w| w == target) {
        return false;
    }
    let mut modified = false;
    let mut i = 0;
    while i <= data.len() - target.len() {
        if &data[i..i + target.len()] == target {
            data[i..i + target.len()].copy_from_slice(replacement);
            modified = true;
            i += target.len();
        } else {
            i += 1;
        }
    }
    modified
}

fn try_create_temp_with_normalized_camera(path: &std::path::Path, err_str: &str) -> Option<NormalizedCamera> {
    let mut data = std::fs::read(path).ok()?;
    let mut modified = false;
    let mut is_5d = false;

    let simple_rules: &[(&str, &[u8], &[u8])] = &[
        ("PENTAX Corporation", b"PENTAX Corporation", b"PENTAX\0           "),
        ("Creo/Leaf", b"<tiff:Make>Creo/Leaf</tiff:Make>", b"<tiff:Make>Leaf</tiff:Make>     "),
        ("K2000", b"PENTAX K2000", b"PENTAX K-m  "),
        ("ILME-FX2", b"ILME-FX2", b"ILME-FX3"),
        ("DMC-GM1S", b"DMC-GM1S", b"DMC-GM1\0"),
        ("FZ45", b"DC-FZ45\0", b"DMC-FZ45"),
        ("NIKON 1 S1", b"NIKON 1 S1", b"NIKON 1 J2"),
        ("NIKON 1 J4", b"NIKON 1 J4", b"NIKON 1 V3"),
        ("NIKON D1H", b"NIKON D1H", b"NIKON D1\0"),
        ("P7700", b"COOLPIX P7700", b"COOLPIX P7800"),
        ("DiMAGE 5", b"DiMAGE 5", b"DiMAGE 7"),
        ("EK-GN120", b"EK-GN120", b"NX300\0\0\0"),
    ];

    for (hint, target, replacement) in simple_rules {
        if replace_bytes_in_place(&mut data, err_str, Some(hint), target, replacement) {
            modified = true;
        }
    }

    // Konica Minolta MRW: 5D -> 7D variants (requires restoring 5D name on the resulting DNG)
    let minolta_pairs: [(&[u8], &[u8]); 4] = [
        (b"DYNAX 5D", b"DYNAX 7D"),
        (b"MAXXUM 5D", b"MAXXUM 7D"),
        (b"ALPHA-5 DIGITAL", b"ALPHA-7 DIGITAL"),
        (b"ALPHA SWEET DIGITAL", b"ALPHA-7 DIGITAL    "),
    ];
    for (target, replacement) in minolta_pairs {
        let hint = std::str::from_utf8(target).ok();
        if replace_bytes_in_place(&mut data, err_str, hint, target, replacement) {
            modified = true;
            is_5d = true;
        }
    }

    // SAMSUNG TECHWIN GX-1L -> PENTAX *ist D
    if replace_bytes_in_place(&mut data, err_str, Some("SAMSUNG TECHWIN"), b"SAMSUNG TECHWIN", b"PENTAX\0        ") {
        modified = true;
        replace_bytes_in_place(&mut data, err_str, None, b"GX-1L", b"*istD");
    }

    // HASSELBLAD Lunar -> SONY NEX-7
    if err_str.contains("Lunar") || (err_str.contains("HASSELBLAD") && data.windows(5).any(|w| w == b"Lunar")) {
        if replace_bytes_in_place(&mut data, err_str, Some("HASSELBLAD"), b"HASSELBLAD", b"SONY\0     ") {
            modified = true;
        }
        if replace_bytes_in_place(&mut data, err_str, Some("Lunar"), b"Lunar", b"NEX-7") {
            modified = true;
        }
    }

    if !modified {
        return None;
    }

    let temp_dir = std::env::temp_dir();
    let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("raw");
    let temp_name = format!(
        "fujify_fix_{}_{}.{}",
        std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).ok()?.as_nanos(),
        ext
    );
    let temp_path = temp_dir.join(temp_name);
    std::fs::write(&temp_path, &data).ok()?;
    Some(NormalizedCamera {
        path: temp_path,
        is_5d_minolta: is_5d,
    })
}

fn restore_minolta_5d_model(path: &std::path::Path) {
    if let Ok(mut dng_data) = std::fs::read(path) {
        let pairs: [(&[u8], &[u8]); 3] = [
            (b"DYNAX 7D", b"DYNAX 5D"),
            (b"MAXXUM 7D", b"MAXXUM 5D"),
            (b"ALPHA-7 DIGITAL", b"ALPHA-5 DIGITAL"),
        ];
        let mut fixed = false;
        for (target, replacement) in pairs {
            for i in 0..dng_data.len().saturating_sub(target.len()) {
                if &dng_data[i..i + target.len()] == target {
                    dng_data[i..i + target.len()].copy_from_slice(replacement);
                    fixed = true;
                }
            }
        }
        if fixed {
            let _ = std::fs::write(path, &dng_data);
        }
    }
}
