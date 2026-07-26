cmake_minimum_required(VERSION 3.20.0)

set(BOARD nrf52840dongle)

find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(cokvm)

target_sources(app PRIVATE
    src/main.c
    src/cokvm_protocol.c
    src/cokvm_ui.c
    src/cokvm_usb.c
    src/cokvm_bt.c
    src/cokvm_shell.c
    src/cokvm_queue.c
)

target_sources_ifdef(CONFIG_COKVM_TESTSUITE app PRIVATE
    src/cokvm_shell_testsuite.c
)
