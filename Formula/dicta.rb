class Dicta < Formula
  desc "English dictation at the cursor"
  version "1.0.0"

  url "file://#{File.expand_path("../packaging/dicta-source.tar.gz", __dir__)}"
  sha256 "de593e1a423dcf01632a97a6df1bc818a3e2ebbd4dbc6edea2cbb89e22d855d4"

  def install
    root = Pathname(__dir__).parent
    if OS.mac?
      rid = Hardware::CPU.arm? ? "osx-arm64" : "osx-x64"
      native = Hardware::CPU.arm? ? "macos-arm64" : "macos-x64"
    elsif Hardware::CPU.arm?
      rid = "linux-arm64"
      native = "linux-arm64"
    else
      rid = "linux-x64"
      native = "linux-x64"
    end

    stage = root/"dist"/rid
    odie "No Dicta build for #{rid} in #{stage}." unless (stage/"Dicta").file?

    libexec.install stage/"Dicta"
    chmod 0755, libexec/"Dicta"
    Dir["#{stage}/libSkiaSharp.*", "#{stage}/libHarfBuzzSharp.*", "#{stage}/libAvaloniaNative.*", "#{stage}/libuiohook.*"].each do |file|
      libexec.install file
    end

    model = [
      stage/"models/ggml-base.en.bin",
      root/"dist/models/ggml-base.en.bin",
      root/".cache/ggml-base.en.bin",
    ].find { |path| path.file? && path.size > 100_000_000 }
    odie "The English model is missing. Expected dist/models/ggml-base.en.bin." if model.nil?

    (libexec/"models").install model
    native_dir = stage/"runtimes"/native
    odie "Whisper libraries are missing from #{native_dir}." unless native_dir.directory?

    (libexec/"runtimes"/native).mkpath
    Dir["#{native_dir}/*"].each do |file|
      (libexec/"runtimes"/native).install file
    end

    (bin/"dicta").write <<~SH
      #!/bin/sh
      exec "#{opt_libexec}/Dicta" "$@"
    SH
    chmod 0755, bin/"dicta"
  end

  def caveats
    <<~EOS
      Start Dicta and keep it running at login with:
        brew services start dicta

      Stop it with:
        brew services stop dicta

      On macOS, allow Dicta under Privacy & Security for Microphone and Accessibility.
    EOS
  end

  service do
    run [opt_libexec/"Dicta"]
    keep_alive true
    working_dir opt_libexec
    log_path var/"log/dicta.log"
    error_log_path var/"log/dicta.log"
  end
end
