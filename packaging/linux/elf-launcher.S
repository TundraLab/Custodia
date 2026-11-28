/*
 * Minimal static x86_64 ELF launcher for Custodia's lightweight installer.
 * Resolves /proc/self/exe and execs python3 to run the payload appended after
 * the magic marker inside this same binary.
 */

.section .rodata
proc_self_exe:
    .asciz "/proc/self/exe"
env_path:
    .asciz "/usr/bin/env"
py_path:
    .asciz "/usr/bin/python3"
py_name:
    .asciz "python3"
flag_c:
    .asciz "-c"
bootstrap_code:
    .asciz "import sys; p=sys.argv[1]; d=open(p,'rb').read().split(b'\\n#===CUSTODIA_PAYLOAD===\\n',1)[1]; exec(compile(d,p,'exec'),{'__file__':p,'__name__':'__main__'})"

.section .bss
    .align 16
exe_buf:
    .skip 4096
    .align 8
new_argv:
    .skip 48

.section .text
    .global _start

_start:
    /* Save argc and compute envp = &argv[argc + 1] */
    movq (%rsp), %rcx
    movq 8(%rsp), %r12              /* argv[0] fallback */
    leaq 16(%rsp, %rcx, 8), %r13    /* envp */

    /* readlink("/proc/self/exe", exe_buf, 4095) */
    movq $89, %rax
    leaq proc_self_exe(%rip), %rdi
    leaq exe_buf(%rip), %rsi
    movq $4095, %rdx
    syscall

    cmpq $0, %rax
    jle use_argv0
    leaq exe_buf(%rip), %r12

use_argv0:
    /* Try execve("/usr/bin/env", ["env", "python3", "-c", bootstrap, self_path, NULL], envp) */
    leaq new_argv(%rip), %rsi
    leaq env_path(%rip), %rax
    movq %rax, 0(%rsi)
    leaq py_name(%rip), %rax
    movq %rax, 8(%rsi)
    leaq flag_c(%rip), %rax
    movq %rax, 16(%rsi)
    leaq bootstrap_code(%rip), %rax
    movq %rax, 24(%rsi)
    movq %r12, 32(%rsi)
    movq $0, 40(%rsi)

    movq $59, %rax                  /* SYS_execve */
    leaq env_path(%rip), %rdi
    movq %r13, %rdx
    syscall

    /* Fallback: execve("/usr/bin/python3", ["python3", "-c", bootstrap, self_path, NULL], envp) */
    leaq new_argv(%rip), %rsi
    leaq py_path(%rip), %rax
    movq %rax, 0(%rsi)
    leaq flag_c(%rip), %rax
    movq %rax, 8(%rsi)
    leaq bootstrap_code(%rip), %rax
    movq %rax, 16(%rsi)
    movq %r12, 24(%rsi)
    movq $0, 32(%rsi)

    movq $59, %rax                  /* SYS_execve */
    leaq py_path(%rip), %rdi
    movq %r13, %rdx
    syscall

    /* Exit with code 127 if python3 could not be launched */
    movq $60, %rax                  /* SYS_exit */
    movq $127, %rdi
    syscall
