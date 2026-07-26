#include <zephyr/shell/shell.h>
#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/usb/class/hid.h>
#include <string.h>
#include <stdlib.h>
#include "cokvm_bt.h"
#include "cokvm_protocol.h"
#include "cokvm_usb.h"
#include "cokvm_queue.h"

LOG_MODULE_REGISTER(cokvm_shell_testsuite, LOG_LEVEL_INF);

#define TEST_INJECT_DELAY_MS 50
#define MOUSE_COORD_MAX 17280

static const uint8_t ascii_to_hid[] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x00-0x07
    0x2a, 0x2b, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x08-0x0f Backspace, Tab, Enter
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x10-0x17
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x18-0x1f
    0x2c, 0x1e, 0x34, 0x20, 0x21, 0x22, 0x24, 0x34, // 0x20-0x27 Space, !, ", #, $, %, &, '
    0x26, 0x27, 0x2d, 0x2e, 0x36, 0x2f, 0x31, 0x30, // 0x28-0x2f (, ), *, +, ,, -, ., /
    0x27, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, // 0x30-0x37 0-7
    0x25, 0x26, 0x33, 0x36, 0x37, 0x38, 0x2d, 0x35, // 0x38-0x3f 8-9, :, ;, <, =, >, ?
    0x14, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, // 0x40-0x47 @, A-G
    0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, // 0x48-0x4f H-O
    0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, // 0x50-0x57 P-W
    0x1b, 0x1c, 0x1d, 0x55, 0x56, 0x54, 0x00, 0x00, // 0x58-0x5f X-Z, [, \, ], ^, _
    0x00, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, // 0x60-0x67 `, a-g
    0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, // 0x68-0x6f h-o
    0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, // 0x70-0x77 p-w
    0x1b, 0x1c, 0x1d, 0x55, 0x56, 0x54, 0x4c, 0x00, // 0x78-0x7f x-z, {, |, }, ~, DEL
};

static uint8_t char_to_keycode(char c)
{
    uint8_t idx = (uint8_t)c;
    if (idx >= sizeof(ascii_to_hid)) {
        return 0;
    }
    return ascii_to_hid[idx];
}

static int inject_kbd_event(uint8_t modifiers, uint8_t keycode)
{
    cokvm_vss_payload_t payload = {
        .magic_k = COKVM_PROTO_MAGIC_K,
        .magic_m = COKVM_PROTO_MAGIC_M,
        .version = COKVM_PROTO_VERSION_0,
        .type = COKVM_MSG_TYPE_KBD_DATA,
    };

    payload.data.kbd.modifiers = modifiers;
    payload.data.kbd.keycodes[0] = keycode;

    return ck_bt_inject_test_data(&payload);
}

int cmd_cokvm_test_kbd(const struct shell *sh, size_t argc, char **argv)
{
    if (argc < 2) {
        shell_error(sh, "Usage: cokvm test kbd \"string\" | capslock | numlock");
        return -EINVAL;
    }

    const char *arg = argv[1];

    if (strcmp(arg, "capslock") == 0) {
        inject_kbd_event(0, HID_KEY_CAPSLOCK);
        k_msleep(TEST_INJECT_DELAY_MS);
        inject_kbd_event(0, 0x00);
        k_msleep(TEST_INJECT_DELAY_MS);
        shell_print(sh, "Injected capslock toggle");
        return 0;
    }

    if (strcmp(arg, "numlock") == 0) {
        inject_kbd_event(0, HID_KEY_NUMLOCK);
        k_msleep(TEST_INJECT_DELAY_MS);
        inject_kbd_event(0, 0x00);
        k_msleep(TEST_INJECT_DELAY_MS);
        shell_print(sh, "Injected numlock toggle");
        return 0;
    }

    size_t len = strlen(arg);

    for (size_t i = 0; i < len; i++) {
        char c = arg[i];
        uint8_t modifiers = 0;
        uint8_t keycode = 0;

        if (c == '\\' && i + 1 < len) {
            i++;
            char next = arg[i];

            switch (next) {
            case 'n': keycode = 0x28; break;
            case 't': keycode = 0x2b; break;
            case 'b': keycode = 0x2a; break;
            case 'e': keycode = 0x29; break;
            case 'c':
                if (i + 1 < len) {
                    i++;
                    char ctrl_key = arg[i];
                    keycode = char_to_keycode(ctrl_key);
                    if (keycode != 0) {
                        modifiers = 0x01;
                    }
                }
                break;
            case '\\': keycode = 0x56; break;
            default: keycode = char_to_keycode(next); break;
            }
        } else {
            keycode = char_to_keycode(c);
            if (keycode != 0 && (c >= 'A' && c <= 'Z')) {
                modifiers = 0x02;
            }
        }

        if (keycode != 0) {
            inject_kbd_event(modifiers, keycode);
            k_msleep(TEST_INJECT_DELAY_MS);
            inject_kbd_event(modifiers, 0x00);
            k_msleep(TEST_INJECT_DELAY_MS);
        }
    }

    shell_print(sh, "Injected keyboard string: %s", arg);
    return 0;
}

int cmd_cokvm_test_mmove(const struct shell *sh, size_t argc, char **argv)
{
    if (argc < 2) {
        shell_error(sh, "Usage: cokvm test mmove x,y");
        return -EINVAL;
    }

    char *token = strtok(argv[1], ",");
    if (!token) {
        shell_error(sh, "Invalid coordinate format");
        return -EINVAL;
    }

    int x = atoi(token);
    token = strtok(NULL, ",");
    if (!token) {
        shell_error(sh, "Invalid coordinate format");
        return -EINVAL;
    }

    int y = atoi(token);

    if (x < 0) x = 0;
    if (x > MOUSE_COORD_MAX) x = MOUSE_COORD_MAX;
    if (y < 0) y = 0;
    if (y > MOUSE_COORD_MAX) y = MOUSE_COORD_MAX;
    cokvm_vss_payload_t payload = {
        .magic_k = COKVM_PROTO_MAGIC_K,
        .magic_m = COKVM_PROTO_MAGIC_M,
        .version = COKVM_PROTO_VERSION_0,
        .type = COKVM_MSG_TYPE_MOUSE_DATA,
    };

    payload.data.mouse.buttons = 0;
    payload.data.mouse.x = (int16_t)x;
    payload.data.mouse.y = (int16_t)y;
    payload.data.mouse.wheel = 0;

    int ret = ck_bt_inject_test_data(&payload);
    if (ret == 0) {
        shell_print(sh, "Mouse moved to (%d, %d)", x, y);
    } else {
        shell_error(sh, "Failed to inject mouse move");
    }

    return ret;
}

static uint8_t parse_mouse_button(const char *btn_str)
{
    if (strcmp(btn_str, "left") == 0) return 0x01;
    if (strcmp(btn_str, "right") == 0) return 0x02;
    if (strcmp(btn_str, "middle") == 0) return 0x04;
    return 0;
}

int cmd_cokvm_test_mbutton(const struct shell *sh, size_t argc, char **argv)
{
    if (argc < 2) {
        shell_error(sh, "Usage: cokvm test mbutton button[,button...]");
        return -EINVAL;
    }

    uint8_t buttons = 0;
    char *token = strtok(argv[1], ",");

    while (token != NULL) {
        uint8_t btn = parse_mouse_button(token);
        if (btn != 0) {
            buttons |= btn;
        }
        token = strtok(NULL, ",");
    }

    if (buttons == 0) {
        shell_error(sh, "No valid button specified");
        return -EINVAL;
    }

    cokvm_vss_payload_t payload = {
        .magic_k = COKVM_PROTO_MAGIC_K,
        .magic_m = COKVM_PROTO_MAGIC_M,
        .version = COKVM_PROTO_VERSION_0,
        .type = COKVM_MSG_TYPE_MOUSE_DATA,
    };

    payload.data.mouse.buttons = buttons;
    payload.data.mouse.x = 0;
    payload.data.mouse.y = 0;
    payload.data.mouse.wheel = 0;

    ck_bt_inject_test_data(&payload);
    k_msleep(TEST_INJECT_DELAY_MS);

    payload.data.mouse.buttons = 0;
    ck_bt_inject_test_data(&payload);

    shell_print(sh, "Mouse button(s) pressed: 0x%02x", buttons);
    return 0;
}

int cmd_cokvm_test_status(const struct shell *sh, size_t argc, char **argv)
{
    ARG_UNUSED(argc);
    ARG_UNUSED(argv);

    struct ck_queue_status qs;
    ck_queue_status(&qs);

    shell_print(sh, "=== CoKVM Status ===");
    shell_print(sh, "USB:");
    shell_print(sh, "  kbd_ready:      %s", kbd_ready ? "true" : "false");
    shell_print(sh, "  mouse_ready:    %s", mouse_ready ? "true" : "false");
    shell_print(sh, "  kbd_led:        0x%02x", kbd_led);
    shell_print(sh, "  kbd_led_syncstate: %d (%s)", kbd_led_syncstate,
                kbd_led_syncstate == INITIALIZING ? "INITIALIZING" :
                kbd_led_syncstate == SYNCING ? "SYNCING" : "SYNCED");
    shell_print(sh, "Queue:");
    shell_print(sh, "  total_blocks:   %u", qs.total_blocks);
    shell_print(sh, "  free_blocks:    %u", qs.free_blocks);
    shell_print(sh, "  used_blocks:    %u", qs.used_blocks);
    shell_print(sh, "  slab_start:     0x%lx", qs.slab_start);
    shell_print(sh, "  slab_end:       0x%lx", qs.slab_end);
    shell_print(sh, "  block_size:     %zu", qs.block_size);
    shell_print(sh, "BT:");
    shell_print(sh, "  connected:      %s", ck_bt_is_connected() ? "true" : "false");

    return 0;
}

int cmd_cokvm_test_peektx(const struct shell *sh, size_t argc, char **argv)
{
    ARG_UNUSED(argc);
    ARG_UNUSED(argv);

    cokvm_txqueue_item_t *item = ck_queue_peek_tx();

    if (!item) {
        shell_print(sh, "USB TX queue is empty");
        return 0;
    }

    shell_print(sh, "USB TX queue head:");
    shell_print(sh, "  item address:   0x%lx", (uintptr_t)item);
    shell_print(sh, "  report_id:      %d", item->data.usb.report_id);

    if (item->data.usb.report_id == COKVM_MSG_KBD_REPORT_ID) {
        shell_print(sh, "  type:           keyboard");
        shell_print(sh, "  modifiers:      0x%02x", item->data.usb.data.kbd.modifiers);
        shell_print(sh, "  keycodes:       0x%02x 0x%02x 0x%02x 0x%02x 0x%02x 0x%02x",
                    item->data.usb.data.kbd.keycodes[0],
                    item->data.usb.data.kbd.keycodes[1],
                    item->data.usb.data.kbd.keycodes[2],
                    item->data.usb.data.kbd.keycodes[3],
                    item->data.usb.data.kbd.keycodes[4],
                    item->data.usb.data.kbd.keycodes[5]);
    } else if (item->data.usb.report_id == COKVM_MSG_MOUSE_REPORT_ID) {
        shell_print(sh, "  type:           mouse");
        shell_print(sh, "  buttons:        0x%02x", item->data.usb.data.mouse.buttons);
        shell_print(sh, "  x:              %d", item->data.usb.data.mouse.x);
        shell_print(sh, "  y:              %d", item->data.usb.data.mouse.y);
        shell_print(sh, "  wheel:          %d", item->data.usb.data.mouse.wheel);
    }

    return 0;
}

int cmd_cokvm_test_kbd(const struct shell *sh, size_t argc, char **argv);
int cmd_cokvm_test_mmove(const struct shell *sh, size_t argc, char **argv);
int cmd_cokvm_test_mbutton(const struct shell *sh, size_t argc, char **argv);
int cmd_cokvm_test_status(const struct shell *sh, size_t argc, char **argv);
int cmd_cokvm_test_peektx(const struct shell *sh, size_t argc, char **argv);
