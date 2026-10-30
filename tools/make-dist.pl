#!/usr/bin/env perl
# Build a distributable THSSS package.
#
# What ships:
#   * the game and the Config program, published for one (or more) runtimes
#   * only the read-only game assets (BGM, Image, Model, FX, Story, CS, Sound, BPic)
#   * the current Setting.INI (no saves/replays)
#   * launcher scripts and a short README
#
# What never ships:
#   * Score.dat / Score.dat.bak, Replay/, ScreenShot/, BPic/ uploads
#   * bin/obj build output, backups, test data, decompiled sources
#
# Usage:
#   perl tools/make-dist.pl                  # linux-x64, framework-dependent
#   perl tools/make-dist.pl --self-contained # bundle the .NET runtime
#   perl tools/make-dist.pl --rid win-x64,osx-arm64
#   perl tools/make-dist.pl --rid osx-arm64 --framework net8.0 --self-contained
#   perl tools/make-dist.pl --skip-build     # reuse previous publish output
#   perl tools/make-dist.pl --out dist/thsss-0.1

use strict;
use warnings;
use Digest::SHA;
use Cwd qw(abs_path);
use Fcntl qw(:mode);
use File::Basename qw(basename dirname);
use File::Find qw(find);
use File::Path qw(make_path remove_tree);
use File::Spec;
use Getopt::Long qw(GetOptions);
use JSON::PP qw(decode_json);
use feature qw(say);
use POSIX qw(strftime);

# Assets the game opens at runtime. Everything else under game/ is player data or
# build noise and must not be distributed.
my @ASSET_DIRS = qw(BGM Image Model FX Sound Story CS BPic);

# Files the game writes into its own directory; never copy these from the dev tree.
my @PLAYER_FILE_PATTERNS = (
    qr/^Score\.dat(\.bak)?$/,
    qr/^Score\.dat\.tmp$/,
);

my $root = abs_path(dirname(__FILE__) . '/..') // die "cannot resolve repository root\n";
chdir $root or die "cannot enter repository root: $!\n";
my %opt = (
    out          => undef,
    rid          => undef,
    configuration => 'Release',
    framework    => $ENV{THSSS_FRAMEWORK},
    'self-contained' => 0,
    'skip-build' => 0,
    clean        => 0,
);
GetOptions(
    'out=s'          => \$opt{out},
    'rid=s'          => \$opt{rid},
    'configuration=s' => \$opt{configuration},
    'framework=s'    => \$opt{framework},
    'self-contained' => \$opt{'self-contained'},
    'skip-build'     => \$opt{'skip-build'},
    'clean'          => \$opt{clean},
) or usage(2);

my $host_framework = default_framework();
$opt{framework} //= $host_framework;
die "unsupported framework: $opt{framework} (use net8.0 or net10.0)\n"
    unless $opt{framework} =~ /^net(?:8|10)\.0$/;
die "net10.0 requires the .NET 10 SDK or newer\n"
    if $opt{framework} eq 'net10.0' && $host_framework eq 'net8.0';
$opt{rid} ||= default_rid();

my @rids = grep { length } split /,/, $opt{rid};
usage(2, "--rid gave no runtimes") unless @rids;

my $stamp = strftime('%Y%m%d-%H%M%S', localtime);
my $out = $opt{out} // File::Spec->catdir($root, 'dist', "thsss-$stamp");
$out = File::Spec->rel2abs($out);

my @required = (
    [ File::Spec->catfile($root, 'src', 'THSSS.csproj'), 'game project' ],
    [ File::Spec->catfile($root, 'src', 'Config', 'THSSS.Config.csproj'), 'config project' ],
    [ File::Spec->catfile($root, 'game', 'Setting.INI'), 'default Setting.INI' ],
    [ File::Spec->catdir($root, 'game', 'Image'), 'game assets' ],
);
for my $entry (@required) {
    die "missing $entry->[1]: $entry->[0]\n" unless -e $entry->[0];
}

for my $rid (@rids) {
    die "unsupported runtime: $rid\n" unless $rid =~ /^(?:linux|win|osx)-(?:x64|arm64)$/;
}
check_assets();
if (grep { /^osx-/ } @rids) {
    for my $tool (qw(glslangValidator spirv-cross)) {
        die "missing build tool: $tool (macOS: brew install glslang spirv-cross)\n" unless which($tool);
    }
}
if ($opt{clean}) {
    # --clean may only remove a resolved child of this repository's dist directory.
    # Resolve links before checking, so a link cannot redirect removal into game/.
    my $dist_path = File::Spec->catdir($root, 'dist');
    die "--clean cannot use a linked dist directory\n" if -l $dist_path;
    my $dist = abs_path($dist_path);
    my $target = abs_path($out);
    if (defined $target && -e $target) {
        die "--clean is restricted to a subdirectory of $root/dist\n" unless defined $dist;
        my $relative = File::Spec->abs2rel($target, $dist);
        die "unsafe --clean target: $target\n"
            if $relative eq '.' || File::Spec->file_name_is_absolute($relative) || $relative =~ m{^\.\.(?:[\\/]|$)};
        check_clean($target);
        remove_tree($target, { error => \my $errors });
        die "cannot clean $target\n" if @$errors;
    }
}

my $total = 0;
for my $rid (@rids) {
    my $stage = @rids == 1 ? $out : File::Spec->catdir($out, $rid);
    say "==> $rid / $opt{framework} -> $stage";

    die "output already exists: $stage; choose --out or use --clean for an old dist package\n"
        if -e $stage && !$opt{'skip-build'};
    check_clean($stage) if -d $stage;

    publish($rid, $stage) unless $opt{'skip-build'};

    die "no published output in $stage (run without --skip-build first)\n"
        unless -f File::Spec->catfile($stage, "THSSS.dll") || -f File::Spec->catfile($stage, "THSSS") || -f File::Spec->catfile($stage, "THSSS.exe");

    check_framework($stage);
    copy_assets($stage);
    if ($rid =~ /^osx-/) {
        # The exporter runs for the build host, even when publishing another RID.
        run('dotnet', 'run', '--project', File::Spec->catfile($root, 'tools', 'MetalShaderExporter', 'MetalShaderExporter.csproj'),
            '--framework', $host_framework, '-c', 'Release', '--', File::Spec->catdir($stage, 'MetalShaders'));
        for my $executable (qw(THSSS Config)) {
            my $path = File::Spec->catfile($stage, $executable);
            die "missing macOS executable: $path\n" unless -f $path;
            chmod 0755, $path or die "cannot make executable: $path: $!\n";
        }
    }
    write_setting($stage);
    write_launchers($stage, $rid);
    write_readme($stage, $rid);
    write_checksums($stage);

    my ($files, $bytes) = measure($stage);
    $total += $bytes;
    printf "     %d files, %.1f MiB\n", $files, $bytes / (1024 * 1024);
}

check_clean($out);
printf "Done: %s (%.1f MiB)\n", $out, $total / (1024 * 1024);
if (@rids == 1 && $rids[0] =~ /^osx-/) {
    run('tar', '-czf', "$out.tar.gz", '-C', dirname($out), basename($out));
    say "Archive: $out.tar.gz";
}
exit 0;

sub usage {
    my ($code, $why) = @_;
    print STDERR "$why\n" if defined $why;
    print STDERR <<"USAGE";
usage: perl tools/make-dist.pl [options]
  --out DIR              output directory (default: dist/thsss-YYYYMMDD-HHMMSS)
  --rid LIST             comma separated runtimes (default: current platform)
  --configuration NAME   Release (default) or Debug
  --framework TFM        net8.0 or net10.0 (default: selected SDK's version)
  --self-contained       bundle the .NET runtime with the game
  --skip-build           reuse existing bin output, do not run dotnet publish
  --clean                delete an old output subdirectory under this repo's dist/
USAGE
    exit $code;
}

# The current machine's RID, so a plain run produces something runnable here.
sub default_rid {
    my $os = $^O;
    return 'linux-x64'   if $os eq 'linux' && !is_arm();
    return 'linux-arm64' if $os eq 'linux' && is_arm();
    return 'osx-arm64'   if $os eq 'darwin' && !is_x86();
    return 'osx-x64'     if $os eq 'darwin';
    return 'win-x64';
}

sub is_arm   { return $ENV{HOSTTYPE} && $ENV{HOSTTYPE} =~ /aarch64|arm64/ }
sub is_x86   { return $ENV{HOSTTYPE} && $ENV{HOSTTYPE} =~ /x86_64/ }

sub default_framework {
    die "dotnet not found in PATH\n" unless which('dotnet');
    open my $sdk, '-|', 'dotnet', '--version' or die "cannot query .NET SDK: $!\n";
    my $version = <$sdk> // '';
    close $sdk or die "dotnet --version failed\n";
    die "unsupported .NET SDK: $version" unless $version =~ /^(\d+)\./ && $1 >= 8;
    return $1 >= 10 ? 'net10.0' : 'net8.0';
}

sub check_framework {
    my ($stage) = @_;
    # In particular, --skip-build must not label an existing net10.0 package as net8.0.
    for my $name (qw(THSSS Config)) {
        my $path = File::Spec->catfile($stage, "$name.runtimeconfig.json");
        open my $fh, '<', $path or die "cannot read published runtime configuration $path: $!\n";
        local $/;
        my $config = decode_json(<$fh>);
        close $fh or die "cannot close $path: $!\n";
        my $framework = $config->{runtimeOptions}{tfm} // '';
        die "$path targets '$framework', expected '$opt{framework}'; publish into a fresh output directory\n"
            unless $framework eq $opt{framework};
    }
}

sub publish {
    my ($rid, $stage) = @_;
    die "dotnet not found in PATH\n" unless which('dotnet');
    run(
        'dotnet', 'publish',
        File::Spec->catfile($root, 'src', 'Config', 'THSSS.Config.csproj'),
        '-c', $opt{configuration},
        '--framework', $opt{framework},
        '-r', $rid,
        ($opt{'self-contained'} ? ('--self-contained', 'true') : ()),
        '-o', $stage,
        '-p:WarningLevel=0',
    );
    # The game publishes into the same directory so the two executables sit side by
    # side, exactly where ConfigWorkspace looks for the game.
    run(
        'dotnet', 'publish',
        File::Spec->catfile($root, 'src', 'THSSS.csproj'),
        '-c', $opt{configuration},
        '--framework', $opt{framework},
        '-r', $rid,
        ($opt{'self-contained'} ? ('--self-contained', 'true') : ()),
        '-o', $stage,
        '-p:WarningLevel=0',
        '-p:GenerateResourceWarnOnBinaryFormatterUse=false',
    );
    return ();
}

sub run {
    say "    @_";
    system(@_) == 0 or die "command failed: @_\n";
}

sub which {
    my ($cmd) = @_;
    for my $dir (File::Spec->path()) {
        for my $name ($cmd, ($^O eq 'MSWin32' ? "$cmd.exe" : ())) {
            my $candidate = File::Spec->catfile($dir, $name);
            return $candidate if -x $candidate;
        }
    }
    return undef;
}

sub check_assets {
    my @pointers;
    for my $dir (@ASSET_DIRS) {
        my $source = File::Spec->catdir($root, 'game', $dir);
        die "missing asset directory: $source\n" unless -d $source || $dir eq 'BPic';
        next unless -d $source;
        find({ no_chdir => 1, wanted => sub {
            return unless -f $_ && -s _ <= 1024;
            open my $fh, '<', $_ or die "cannot read $_: $!\n";
            my $header = <$fh> // '';
            close $fh;
            push @pointers, File::Spec->abs2rel($_, $root)
                if $header =~ /^version https:\/\/git-lfs\.github\.com\/spec\/v1\r?\n?$/;
        }}, $source);
    }
    if (@pointers) {
        my @examples = @pointers[0 .. ($#pointers < 4 ? $#pointers : 4)];
        die scalar(@pointers) . " assets are Git LFS pointers, not game data:\n  "
            . join("\n  ", @examples)
            . "\nRun git lfs install and git lfs pull in the repository before packaging.\n";
    }
}

sub copy_assets {
    my ($stage) = @_;
    die "output directory does not exist: $stage\n" unless -d $stage;
    for my $dir (@ASSET_DIRS) {
        my $src = File::Spec->catdir($root, 'game', $dir);
        next unless -d $src;
        my $dest = File::Spec->catdir($stage, $dir);
        make_path($dest);
        my $copied = 0;
        find({
            no_chdir => 1,
            wanted   => sub {
                return unless -f $_;
                my $name = basename($_);
                for my $re (@PLAYER_FILE_PATTERNS) {
                    if ($name =~ $re) {
                        warn "    skip player file: $_\n";
                        return;
                    }
                }
                my $rel = File::Spec->abs2rel($_, $src);
                my $target = File::Spec->catfile($dest, $rel);
                make_path(dirname($target));
                copy_file($_, $target);
                $copied++;
            },
        }, $src);
        printf "    %-6s %d files\n", $dir, $copied;
    }

    # Empty runtime directories the game writes into. They exist in a fresh install
    # and the game does not create them itself for every path.
    for my $dir (qw(Replay ScreenShot BPic)) {
        make_path(File::Spec->catdir($stage, $dir));
        my $keep = File::Spec->catfile($stage, $dir, '.gitkeep');
        open my $fh, '>', $keep or die "cannot write $keep: $!";
        close $fh;
    }
}

sub write_setting {
    my ($stage) = @_;
    my $src = File::Spec->catfile($root, 'game', 'Setting.INI');
    my $dest = File::Spec->catfile($stage, 'Setting.INI');
    copy_file($src, $dest);
    say "    Setting.INI copied from game/ (current settings)";
}

sub write_launchers {
    my ($stage, $rid) = @_;
    my $is_windows = $rid =~ /^win/;
    my $game = $is_windows ? 'THSSS.exe' : 'THSSS';
    my $config = $is_windows ? 'Config.exe' : 'Config';

    if ($is_windows) {
        # Config.exe starts the game itself; a .bat mirrors that for the game alone.
        write_file(File::Spec->catfile($stage, 'run-game.bat'),
            "\@echo off\r\ncd /d \"%~dp0\"\r\n\"$game\" %*\r\n");
        write_file(File::Spec->catfile($stage, 'run-config.bat'),
            "\@echo off\r\ncd /d \"%~dp0\"\r\n\"$config\" %*\r\n");
    } else {
        write_file(File::Spec->catfile($stage, 'run-game.sh'), <<"SH", 0755);
#!/bin/sh
# Start the game from its own directory so assets and saves resolve locally.
cd "\$(dirname "\$0")" || exit 1
exec "./$game" "\$\@"
SH
        write_file(File::Spec->catfile($stage, 'run-config.sh'), <<"SH", 0755);
#!/bin/sh
cd "\$(dirname "\$0")" || exit 1
exec "./$config" "\$\@"
SH
        if ($rid =~ /^osx-/) {
            for my $name (qw(game config)) {
                write_file(File::Spec->catfile($stage, "run-$name.command"),
                    "#!/bin/sh\ncd \"\$(dirname \"\$0\")\" || exit 1\nexec /bin/sh \"./run-$name.sh\" \"\$\@\"\n", 0755);
            }
        }
    }
    say "    launchers written for $rid";
}

sub write_readme {
    my ($stage, $rid) = @_;
    my $is_windows = $rid =~ /^win/;
    my $how = $is_windows
        ? "Double-click Config.exe to change settings or run-game.bat to start directly."
        : "Run ./run-config.sh to change settings or ./run-game.sh to start directly.";
    my ($runtime_version) = $opt{framework} =~ /^net(\d+\.\d+)$/;
    my $req = $opt{'self-contained'}
        ? 'This build bundles the .NET runtime.'
        : "Requires the .NET $runtime_version runtime to be installed.";
    my $platform_req = $rid =~ /^osx-/
        ? 'Uses native Metal and AudioToolbox on macOS. Cached MSL shaders and the built-in Metal compiler require no external tools at runtime.'
        : $is_windows
            ? 'Requires OpenGL and XAudio2 2.9 on the system.'
            : 'Requires OpenGL and OpenAL (for example openal-soft) on the system.';
    write_file(File::Spec->catfile($stage, 'README.txt'), <<"TXT");
TOUHOU Hisou Sousou / 东方夏夜祭 - cross-platform port

$how
$req

$platform_req

Settings live in Setting.INI next to this file. Saves (Score.dat), replays
(Replay/) and screenshots (ScreenShot/) are created here on first use and are
not part of this package.

Built for: $rid
Target framework: $opt{framework}
TXT
}

sub write_file {
    my ($path, $content, $mode) = @_;
    open my $fh, '>', $path or die "cannot write $path: $!";
    binmode $fh;
    print {$fh} $content;
    close $fh or die "cannot close $path: $!";
    chmod $mode, $path if defined $mode;
}

sub copy_file {
    my ($from, $to) = @_;
    open my $in, '<', $from or die "cannot read $from: $!";
    binmode $in;
    open my $out, '>', $to or die "cannot write $to: $!";
    binmode $out;
    my $buffer;
    while (1) {
        my $read = read $in, $buffer, 1024 * 1024;
        die "read error on $from: $!" unless defined $read;
        last unless $read;
        print {$out} $buffer or die "write error on $to: $!";
    }
    close $out or die "cannot close $to: $!";
    close $in;
    my $mode = (stat $from)[2] & 07777;
    chmod $mode, $to;
}

# SHA-256 manifest so a downloaded package can be verified without trusting us.
sub write_checksums {
    my ($stage) = @_;
    my $manifest = File::Spec->catfile($stage, 'SHA256SUMS.txt');
    my @files;
    find({
        no_chdir => 1,
        wanted   => sub {
            return unless -f $_;
            my $rel = File::Spec->abs2rel($_, $stage);
            return if $rel eq 'SHA256SUMS.txt';
            push @files, [$rel, $_];
        },
    }, $stage);
    @files = sort { $a->[0] cmp $b->[0] } @files;
    open my $fh, '>', $manifest or die "cannot write $manifest: $!";
    binmode $fh;
    for my $entry (@files) {
        open my $in, '<', $entry->[1] or die "cannot read $entry->[1]: $!";
        binmode $in;
        my $sha = Digest::SHA->new(256);
        $sha->addfile($in);
        close $in;
        printf {$fh} "%s  %s\n", $sha->hexdigest, $entry->[0];
    }
    close $fh or die "cannot close $manifest: $!";
    printf "    SHA256SUMS.txt: %d entries\n", scalar @files;
}

sub measure {
    my ($dir) = @_;
    my ($files, $bytes) = (0, 0);
    find({
        no_chdir => 1,
        wanted   => sub {
            return unless -f $_;
            $files++;
            $bytes += -s $_;
        },
    }, $dir);
    return ($files, $bytes);
}

# Fail loudly if a player file or build artefact slipped into the package.
sub check_clean {
    my ($dir) = @_;
    my @bad;
    find({
        no_chdir => 1,
        wanted   => sub {
            my $path = $_;
            my $rel = File::Spec->abs2rel($path, $dir);
            $rel =~ s{\\}{/}g;
            if ($rel =~ m{(?:^|/)Score\.dat(?:\.bak|\.tmp)?$}
                || $rel =~ m{\.tmp$}
                || ($rel =~ m{(?:^|/)(Replay|ScreenShot)/.+} && basename($rel) ne '.gitkeep')
                || $rel =~ m{(?:^|/)(bin|obj)(?:/|$)}) {
                push @bad, $rel;
            }
        },
    }, $dir);
    die "package contains private or build files: @bad\n" if @bad;
    say '    clean: no Score/Replay/screenshots/bin/obj in package';
}
