#!/usr/bin/env python3
"""
PTY helper script that creates a proper pseudo-terminal with resize support.
Communicates resize commands via a control protocol on stdin.
Linux adaptation of the macOS pty_helper.py.
"""
import os
import sys
import pty
import fcntl
import struct
import termios
import select
import signal

def set_winsize(fd, rows, cols):
    """Set the window size of a PTY."""
    winsize = struct.pack('HHHH', rows, cols, 0, 0)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, winsize)

def main():
    if len(sys.argv) < 4:
        print("Usage: pty_helper.py <cols> <rows> <shell> [args...]", file=sys.stderr)
        sys.exit(1)

    cols = int(sys.argv[1])
    rows = int(sys.argv[2])
    shell = sys.argv[3]
    shell_args = sys.argv[3:]

    # Create PTY
    master_fd, slave_fd = pty.openpty()

    # Set initial size
    set_winsize(master_fd, rows, cols)

    # Fork
    pid = os.fork()

    if pid == 0:
        # Child process
        os.close(master_fd)
        os.setsid()

        # Set slave as controlling terminal
        fcntl.ioctl(slave_fd, termios.TIOCSCTTY, 0)

        # Redirect stdio
        os.dup2(slave_fd, 0)
        os.dup2(slave_fd, 1)
        os.dup2(slave_fd, 2)

        if slave_fd > 2:
            os.close(slave_fd)

        # Set environment
        home = os.path.expanduser('~')
        os.environ['TERM'] = 'xterm-256color'
        os.environ['COLORTERM'] = 'truecolor'
        os.environ['COLUMNS'] = str(cols)
        os.environ['LINES'] = str(rows)
        os.environ['HOME'] = home
        os.environ['USER'] = os.environ.get('USER', os.path.basename(home))

        # Set up NVM environment so it initializes correctly in the shell
        nvm_dir = f'{home}/.nvm'
        if os.path.isdir(nvm_dir):
            os.environ['NVM_DIR'] = nvm_dir

            # Find the latest node version and set NVM_BIN so NVM uses it
            nvm_versions_dir = f'{nvm_dir}/versions/node'
            if os.path.isdir(nvm_versions_dir):
                try:
                    def parse_version(v):
                        try:
                            parts = v.lstrip('v').split('.')
                            return tuple(int(p) for p in parts)
                        except (ValueError, AttributeError):
                            return (0, 0, 0)

                    versions = sorted(os.listdir(nvm_versions_dir), key=parse_version, reverse=True)
                    for version in versions:
                        node_bin = f'{nvm_versions_dir}/{version}/bin'
                        if os.path.isdir(node_bin):
                            # Set NVM environment variables so nvm.sh uses this version
                            os.environ['NVM_BIN'] = node_bin
                            os.environ['NVM_INC'] = f'{nvm_versions_dir}/{version}/include/node'
                            break
                except OSError:
                    pass

        # Ensure PATH includes common Linux locations
        additional_paths = []

        # Add user-configured custom paths first (highest priority)
        custom_paths_env = os.environ.get('TERMINALHOST_CUSTOM_PATHS', '')
        if custom_paths_env:
            additional_paths.extend(p for p in custom_paths_env.split(':') if p)

        # Add default paths
        additional_paths.extend([
            f'{home}/.local/bin',           # Claude CLI, user-installed tools
            '/usr/local/bin',               # User-installed tools
            '/usr/local/sbin',
            '/usr/bin',
            '/bin',
            '/usr/sbin',
            '/sbin',
            f'{home}/.cargo/bin',           # Rust tools
            f'{home}/.npm-global/bin',      # npm global packages
            '/snap/bin',                    # Snap packages
        ])

        # Add NVM node paths if available (find latest version using semantic versioning)
        nvm_versions_dir = f'{home}/.nvm/versions/node'
        if os.path.isdir(nvm_versions_dir):
            try:
                def parse_version(v):
                    """Parse version like 'v24.6.0' into tuple (24, 6, 0) for proper sorting"""
                    try:
                        # Remove 'v' prefix and split by '.'
                        parts = v.lstrip('v').split('.')
                        return tuple(int(p) for p in parts)
                    except (ValueError, AttributeError):
                        return (0, 0, 0)

                versions = sorted(os.listdir(nvm_versions_dir), key=parse_version, reverse=True)
                for version in versions:
                    node_bin = f'{nvm_versions_dir}/{version}/bin'
                    if os.path.isdir(node_bin):
                        additional_paths.insert(0, node_bin)
                        break
            except OSError:
                pass

        current_path = os.environ.get('PATH', '')
        path_parts = current_path.split(':') if current_path else []

        # Add paths in reverse order so first items in additional_paths end up first in PATH
        for path in reversed(additional_paths):
            if path not in path_parts and os.path.isdir(path):
                path_parts.insert(0, path)

        os.environ['PATH'] = ':'.join(path_parts)

        # Ensure we're in a valid working directory
        try:
            os.getcwd()
        except OSError:
            # Current directory is inaccessible, change to home
            try:
                os.chdir(home)
            except OSError:
                os.chdir('/tmp')

        # Execute shell
        os.execvp(shell, shell_args)

    # Parent process
    os.close(slave_fd)

    # Make stdin non-blocking
    flags = fcntl.fcntl(sys.stdin.fileno(), fcntl.F_GETFL)
    fcntl.fcntl(sys.stdin.fileno(), fcntl.F_SETFL, flags | os.O_NONBLOCK)

    # Control sequence for resize: ESC ] 7 7 7 ; <cols> ; <rows> BEL
    # We use a custom escape sequence that won't conflict with terminal output
    RESIZE_PREFIX = b'\x1b]777;'
    RESIZE_SUFFIX = b'\x07'

    input_buffer = b''

    try:
        while True:
            # Wait for data from stdin or master
            rlist, _, _ = select.select([sys.stdin.fileno(), master_fd], [], [], 0.1)

            # Check if child is still alive
            result = os.waitpid(pid, os.WNOHANG)
            if result[0] != 0:
                break

            for fd in rlist:
                if fd == sys.stdin.fileno():
                    # Data from .NET app
                    try:
                        data = os.read(sys.stdin.fileno(), 4096)
                        if not data:
                            continue

                        # Check for resize command in the data
                        input_buffer += data

                        while True:
                            # Look for resize sequence
                            start = input_buffer.find(RESIZE_PREFIX)
                            if start == -1:
                                # No resize command, send everything to PTY
                                if input_buffer:
                                    os.write(master_fd, input_buffer)
                                    input_buffer = b''
                                break

                            # Send data before resize command to PTY
                            if start > 0:
                                os.write(master_fd, input_buffer[:start])

                            # Find end of resize command
                            end = input_buffer.find(RESIZE_SUFFIX, start)
                            if end == -1:
                                # Incomplete resize command, keep in buffer
                                input_buffer = input_buffer[start:]
                                break

                            # Parse resize command: ESC]777;<cols>;<rows>BEL
                            resize_data = input_buffer[start + len(RESIZE_PREFIX):end]
                            input_buffer = input_buffer[end + 1:]

                            try:
                                parts = resize_data.decode('ascii').split(';')
                                if len(parts) == 2:
                                    new_cols = int(parts[0])
                                    new_rows = int(parts[1])
                                    set_winsize(master_fd, new_rows, new_cols)
                                    os.kill(pid, signal.SIGWINCH)
                            except (ValueError, OSError):
                                pass
                    except BlockingIOError:
                        pass

                elif fd == master_fd:
                    # Data from PTY
                    try:
                        data = os.read(master_fd, 4096)
                        if data:
                            sys.stdout.buffer.write(data)
                            sys.stdout.buffer.flush()
                    except OSError:
                        break

    except KeyboardInterrupt:
        pass
    finally:
        os.close(master_fd)
        try:
            os.kill(pid, signal.SIGTERM)
            os.waitpid(pid, 0)
        except:
            pass

if __name__ == '__main__':
    main()