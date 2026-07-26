#include <stdint.h>
#include <string.h>
#include <zephyr/kernel.h>
#include <zephyr/device.h>
#include <zephyr/usb/usbd.h>
#include <zephyr/usb/class/usbd_hid.h>
#include <zephyr/sys/util.h>
#include <zephyr/logging/log.h>
#include "cokvm_protocol.h"
#include "cokvm_usb.h"
#include "cokvm_queue.h"

LOG_MODULE_REGISTER(cokvm_usb, LOG_LEVEL_INF);

#define MOUSE_THROTTLE_MS 3000

struct k_spinlock led_sync_lock;

uint8_t kbd_led = 0;
state_t kbd_led_syncstate = INITIALIZING;
uint8_t sync_retry_count = 0;
bool kbd_ready = false;
bool mouse_ready = false;
static bool mouse_throttling = false;
static uint32_t mouse_throttle_start_ms = 0;

static const struct device *kbd_dev;
static const struct device *mouse_dev;

static const uint8_t kbd_report_desc[] = {
    0x05, 0x01,  // Usage Page (Generic Desktop)
    0x09, 0x06,  // Usage (Keyboard)
    0xa1, 0x01,  // Collection (Application)
    0x85, 0x01,  //   Report ID (1)
    0x05, 0x07,  //   Usage Page (Keyboard/Keypad)
    0x19, 0xe0,  //   Usage Minimum (Left Control)
    0x29, 0xe7,  //   Usage Maximum (Right GUI)
    0x15, 0x00,  //   Logical Minimum (0)
    0x25, 0x01,  //   Logical Maximum (1)
    0x75, 0x01,  //   Report Size (1)
    0x95, 0x08,  //   Report Count (8)
    0x81, 0x02,  //   Input (Data,Var,Abs)
    0x95, 0x01,  //   Report Count (1)
    0x75, 0x08,  //   Report Size (8)
    0x81, 0x03,  //   Input (Const)
    0x95, 0x05,  //   Report Count (5)
    0x75, 0x01,  //   Report Size (1)
    0x05, 0x08,  //   Usage Page (LEDs)
    0x19, 0x01,  //   Usage Minimum (Num Lock)
    0x29, 0x05,  //   Usage Maximum (Kana)
    0x91, 0x02,  //   Output (Data,Var,Abs)
    0x95, 0x01,  //   Report Count (1)
    0x75, 0x03,  //   Report Size (3)
    0x91, 0x03,  //   Output (Const)
    0x95, 0x06,  //   Report Count (6)
    0x75, 0x08,  //   Report Size (8)
    0x15, 0x00,  //   Logical Minimum (0)
    0x25, 0x65,  //   Logical Maximum (101)
    0x05, 0x07,  //   Usage Page (Keyboard/Keypad)
    0x19, 0x00,  //   Usage Minimum (Reserved)
    0x29, 0x65,  //   Usage Maximum (Keyboard Application)
    0x81, 0x00,  //   Input (Data,Ary)
    0xc0         // End Collection
};

static const uint8_t mouse_report_desc[] = {
    0x05, 0x01,        // Usage Page (Generic Desktop)
    0x09, 0x02,        // Usage (Mouse)
    0xa1, 0x01,        // Collection (Application)
    0x09, 0x01,        //   Usage (Pointer)
    0xa1, 0x00,        //   Collection (Physical)
    0x85, 0x02,        //     Report ID (2)
    0x05, 0x09,        //     Usage Page (Button)
    0x19, 0x01,        //     Usage Minimum (1)
    0x29, 0x05,        //     Usage Maximum (5)
    0x15, 0x00,        //     Logical Minimum (0)
    0x25, 0x01,        //     Logical Maximum (1)
    0x95, 0x05,        //     Report Count (5)
    0x75, 0x01,        //     Report Size (1)
    0x81, 0x02,        //     Input (Data,Var,Abs)
    0x95, 0x01,        //     Report Count (1)
    0x75, 0x03,        //     Report Size (3)
    0x81, 0x03,        //     Input (Cnst,Var,Abs)
    0x05, 0x01,        //     Usage Page (Generic Desktop)
    0x09, 0x30,        //     Usage (X)
    0x09, 0x31,        //     Usage (Y)
    0x16, 0x00, 0x00,  //     Logical Minimum (0)
    0x26, 0x80, 0x43,  //     Logical Maximum (17280)
    0x75, 0x10,        //     Report Size (16)
    0x95, 0x02,        //     Report Count (2)
    0x81, 0x02,        //     Input (Data,Var,Abs)
    0x09, 0x38,        //     Usage (Wheel)
    0x15, 0x81,        //     Logical Minimum (-127)
    0x25, 0x7f,        //     Logical Maximum (127)
    0x75, 0x08,        //     Report Size (8)
    0x95, 0x01,        //     Report Count (1)
    0x81, 0x06,        //     Input (Data,Var,Rel)
    0xc0,              //   End Collection
    0xc0               // End Collection
};

static void set_protocol(const struct device *dev, const uint8_t proto)
{
    ARG_UNUSED(dev);

    LOG_INF("protocol changed to %s", proto == 0U ? "boot" : "report");
}

static void kbd_iface_ready(const struct device *dev, const bool ready)
{
    ARG_UNUSED(dev);

    kbd_ready = ready;
    LOG_INF("kbd interface %s", ready ? "ready" : "not ready");
}

static int kbd_get_report(const struct device *dev, const uint8_t type,
                             const uint8_t id, const uint16_t len, uint8_t *const buffer)
{
    ARG_UNUSED(dev);
    ARG_UNUSED(type);
    ARG_UNUSED(id);
    ARG_UNUSED(len);
    ARG_UNUSED(buffer);

    return -ENOTSUP;
}

static int kbd_set_report(const struct device *dev, const uint8_t type,
                             const uint8_t id, const uint16_t len, const uint8_t *const buffer)
{
    ARG_UNUSED(dev);
    ARG_UNUSED(type);
    ARG_UNUSED(id);
    ARG_UNUSED(len);

    if (len == 0 || !buffer) {
        return -EINVAL;
    }

    uint8_t led_status = buffer[1] & COKVM_KBD_LED_MASK;
    LOG_INF("received host led: 0x%02x, current: 0x%02x, syncstate: %d", led_status, kbd_led, kbd_led_syncstate);

    k_spinlock_key_t key = k_spin_lock(&led_sync_lock);

    switch (kbd_led_syncstate) {
    case INITIALIZING:
        kbd_led = led_status;
        kbd_led_syncstate = SYNCED;
        LOG_INF("LED status saved during initialization: 0x%02x", led_status);
        break;
    case SYNCING:
        if (led_status == kbd_led) {
            // Sync successful
            kbd_led_syncstate = SYNCED;
            sync_retry_count = 0;
            LOG_INF("LED synced: 0x%02x", led_status);
        } else if (sync_retry_count < MAX_RETRY) {
            // Retry sync
            uint8_t diff = led_status ^ kbd_led;
            sync_retry_count++;
            LOG_INF("LED sync retry %d/%d (diff: 0x%02x)",
                     sync_retry_count, MAX_RETRY, diff);
            // Save diff for use outside the lock
            uint8_t saved_diff = diff;
            k_spin_unlock(&led_sync_lock, key);
            ck_usb_tx_submit_led(saved_diff);
            return 0;
        } else {
            // Max retries exceeded; accept host state
            LOG_WRN("LED sync timeout, accepting host status: 0x%02x (expected: 0x%02x)",
                    led_status, kbd_led);
            kbd_led = led_status;
            kbd_led_syncstate = SYNCED;
            sync_retry_count = 0;
        }
        break;
    case SYNCED:
        // Ignore mismatch until the next BLE update
        if (led_status != kbd_led) {
            LOG_INF("LED status drifted in SYNCED: target=0x%02x, host=0x%02x (ignored)",
                    kbd_led, led_status);
        }
        break;
    }

    k_spin_unlock(&led_sync_lock, key);
    return 0;
}

static void kbd_output_report(const struct device *dev,
                                 const uint16_t len, const uint8_t *const buffer)
{
    ARG_UNUSED(dev);

    kbd_set_report(dev, HID_REPORT_TYPE_OUTPUT, COKVM_MSG_KBD_REPORT_ID, len -1, &buffer[1]);
}

static void mouse_iface_ready(const struct device *dev, const bool ready)
{
    ARG_UNUSED(dev);

    mouse_ready = ready;
    LOG_INF("mouse interface %s", ready ? "ready" : "not ready");
}

static int mouse_get_report(const struct device *dev, const uint8_t type,
                               const uint8_t id, const uint16_t len, uint8_t *const buffer)
{
    ARG_UNUSED(dev);
    ARG_UNUSED(type);
    ARG_UNUSED(id);
    ARG_UNUSED(len);
    ARG_UNUSED(buffer);

    return -ENOTSUP;
}

static void kbd_input_report_done(const struct device *dev, const uint8_t *const report)
{
    ARG_UNUSED(dev);
    ck_free_queue_item_from_report((const cokvm_usb_payload_t *)report);
}

static void mouse_input_report_done(const struct device *dev, const uint8_t *const report)
{
    ARG_UNUSED(dev);
    ck_free_queue_item_from_report((const cokvm_usb_payload_t *)report);
}

static const struct hid_device_ops kbd_ops = {
    .iface_ready = kbd_iface_ready,
    .get_report = kbd_get_report,
    .set_report = kbd_set_report,
    .set_protocol = set_protocol,
    .output_report = kbd_output_report,
    .input_report_done = kbd_input_report_done,
};

static const struct hid_device_ops mouse_ops = {
    .iface_ready = mouse_iface_ready,
    .get_report = mouse_get_report,
    .set_protocol = set_protocol,
    .input_report_done = mouse_input_report_done,
};

USBD_DEVICE_DEFINE(cokvm_usbd_ctx, DEVICE_DT_GET(DT_NODELABEL(zephyr_udc0)), 0x1915, 0x5211);
USBD_DESC_LANG_DEFINE(cokvm_lang);
USBD_DESC_MANUFACTURER_DEFINE(cokvm_mfr, "CoKVM Project");
USBD_DESC_PRODUCT_DEFINE(cokvm_product, "CoKVM");
USBD_DESC_CONFIG_DEFINE(cokvm_fs_cfg_desc, "CoKVM FS Config");
USBD_CONFIGURATION_DEFINE(cokvm_fs_config, 0, 100, &cokvm_fs_cfg_desc);

int ck_usb_init(void)
{
    int ret;

    kbd_dev = DEVICE_DT_GET(DT_NODELABEL(hid_kbd));
    mouse_dev = DEVICE_DT_GET(DT_NODELABEL(hid_mouse));

    if (!device_is_ready(kbd_dev) || !device_is_ready(mouse_dev)) {
        return -ENODEV;
    }

    hid_device_register(kbd_dev, kbd_report_desc, sizeof(kbd_report_desc), &kbd_ops);
    hid_device_register(mouse_dev, mouse_report_desc, sizeof(mouse_report_desc), &mouse_ops);

    usbd_add_descriptor(&cokvm_usbd_ctx, &cokvm_lang);
    usbd_add_descriptor(&cokvm_usbd_ctx, &cokvm_mfr);
    usbd_add_descriptor(&cokvm_usbd_ctx, &cokvm_product);

    ret = usbd_add_configuration(&cokvm_usbd_ctx, USBD_SPEED_FS, &cokvm_fs_config);
    if (ret) {
        return ret;
    }

    usbd_register_class(&cokvm_usbd_ctx, "cdc_acm_0", USBD_SPEED_FS, 1);
    usbd_register_class(&cokvm_usbd_ctx, "hid_0", USBD_SPEED_FS, 1);
    usbd_register_class(&cokvm_usbd_ctx, "hid_1", USBD_SPEED_FS, 1);

    usbd_device_set_code_triple(&cokvm_usbd_ctx, USBD_SPEED_FS, 0xef, 0x02, 0x01);
    ret = usbd_init(&cokvm_usbd_ctx);
    if (ret) {
        return ret;
    }

    return usbd_enable(&cokvm_usbd_ctx);
}

bool ck_usb_mouse_throttled(void)
{
    if (!mouse_throttling) {
        return false;
    }
    if (k_uptime_get_32() - mouse_throttle_start_ms >= MOUSE_THROTTLE_MS) {
        mouse_throttling = false;
        return false;
    }
    return true;
}

// send usb payload to host
int ck_usb_send(const cokvm_usb_payload_t *payload)
{
    uint8_t report_id = payload->report_id;
    int ret;
    
    if (report_id == COKVM_MSG_KBD_REPORT_ID) {
        if (!kbd_ready) {
            return -ENODEV;
        }
        ret = hid_device_submit_report(kbd_dev, sizeof(cokvm_kbd_data_t) + 1, (const uint8_t * const) payload);
        if (ret) {
            LOG_WRN("usb_send: kbd report submit failed: %d", ret);
            return ret;
        }
    } else if (report_id == COKVM_MSG_MOUSE_REPORT_ID) {
        if (!mouse_ready) {
            return -ENODEV;
        }
        ret = hid_device_submit_report(mouse_dev, sizeof(cokvm_mouse_data_t) + 1, (const uint8_t * const) payload);
        if (ret) {
            mouse_throttling = true;
            mouse_throttle_start_ms = k_uptime_get_32();
            return ret;
        }
    } else {
        LOG_WRN("usb_send: unknown report id %d", report_id);    
        return -EINVAL;
    }
    return 0;
}
