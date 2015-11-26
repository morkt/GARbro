#!/usr/bin/perl
#
# increment .Net assembly revision

use strict;
use Win32;
use File::Basename;
use File::Spec;
use File::Temp;

sub get_git_exe {
    my $user_app_data = Win32::GetFolderPath (Win32::CSIDL_LOCAL_APPDATA);
    my $git_glob = File::Spec->catfile ($user_app_data, 'GitHub', 'PortableGit_*', 'cmd', 'git.exe');
    my $git_path = glob ($git_glob);
    die "PortableGit not found\n" unless -x $git_path;
    return $git_path;
}

sub match_version {
    return $_[0] =~ /^(\d+)\.(\d+)(?:\.(\d+)\.(\d+))?/;
}

unless (1 == $#ARGV) {
    print "usage: inc-revision.pl PROJECT-FILE CONFIG\n";
    exit 0;
}

my ($project_path, $config) = @ARGV;
my $project_dir = dirname ($project_path);
my $project_file = basename ($project_path);
my $is_release = 'release' eq lc $config;
chdir $project_dir or die "$project_dir: $!\n";

my $git_exe = get_git_exe;
my $prop_dir = File::Spec->catfile ('.', 'Properties');
my $assembly_info = File::Spec->catfile ($prop_dir, 'AssemblyInfo.cs');
my $revision = `$git_exe rev-list HEAD --count .`;
die "git.exe failed\n" if $? != 0;
chomp $revision;

my $version_changed = 0;
my $tmp_filename;
{
    open (my $assembly_file, '<', $assembly_info) or die "${assembly_info}: $!";
    my $tmp_output = File::Temp->new (DIR => $prop_dir, UNLINK => 0);
    $tmp_filename = $tmp_output->filename;
    binmode ($tmp_output, ':crlf');

    while (<$assembly_file>) {
        m,^//, and next;
        /^\[assembly:\s*(Assembly(?:File)?Version)\s*\("(.*?)"\)\]/ and do {
            my ($attr, $version) = ($1, $2);
            my ($major, $minor, $build, $rev) = match_version ($version);
            $build += 1 if $is_release;
            my $new_version = "${major}.${minor}.${build}.${revision}";
            $_ = "[assembly: ${attr} (\"${new_version}\")]\n";
            print "AssemblyVersion: $new_version\n" if $attr eq 'AssemblyVersion';
            $version_changed ||= $version ne $new_version;
        };
    } continue {
        print $tmp_output $_;
    }
}

if ($version_changed) {
    rename $assembly_info, "${assembly_info}~" or die "${assembly_info}: $!";
    rename $tmp_filename, $assembly_info or die "${tmp_filename}: $!";
} else {
    unlink $tmp_filename;
}
