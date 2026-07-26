#include <zephyr/kernel.h>
#include <zephyr/drivers/gpio.h>
#include <zephyr/logging/log.h>
#include "cokvm_ui.h"

LOG_MODULE_REGISTER(cokvm_ui, LOG_LEVEL_INF);

static volatile enum cokvm_ui_status ui_status = COKVM_UI_STATUS_NORMAL;

static const struct gpio_dt_spec led_mono      = GPIO_DT_SPEC_GET(DT_ALIAS(led0), gpios);
static const struct gpio_dt_spec led_rgb_red    = GPIO_DT_SPEC_GET(DT_NODELABEL(led1_red), gpios);
static const struct gpio_dt_spec led_rgb_green  = GPIO_DT_SPEC_GET(DT_NODELABEL(led1_green), gpios);
static const struct gpio_dt_spec led_rgb_blue   = GPIO_DT_SPEC_GET(DT_NODELABEL(led1_blue), gpios);

static const char *const ui_status_str[] = {
    "normal",
    "pairing",
    "connected",
    "error"
};

void ck_ui_init(void)
{
    gpio_pin_configure_dt(&led_mono, GPIO_OUTPUT_INACTIVE);
    gpio_pin_configure_dt(&led_rgb_red, GPIO_OUTPUT_INACTIVE);
    gpio_pin_configure_dt(&led_rgb_green, GPIO_OUTPUT_INACTIVE);
    gpio_pin_configure_dt(&led_rgb_blue, GPIO_OUTPUT_INACTIVE);
}

void ck_ui_set_status(enum cokvm_ui_status status)
{
    ui_status = status;
    LOG_INF("status updated to: %s", ui_status_str[status]);
}

enum cokvm_ui_status ck_ui_get_status(void)
{
    return ui_status;
}

void ck_ui_loop(void)
{
    switch (ui_status) {
    case COKVM_UI_STATUS_PAIRING:
        gpio_pin_set_dt(&led_rgb_red, 0);
        gpio_pin_set_dt(&led_rgb_green, 0);
        gpio_pin_toggle_dt(&led_rgb_blue);
        k_msleep(200);
        break;

    case COKVM_UI_STATUS_CONNECTED:
        gpio_pin_set_dt(&led_rgb_red, 0);
        gpio_pin_set_dt(&led_rgb_blue, 0);
        gpio_pin_set_dt(&led_rgb_green, 1);
        k_msleep(1000);
        break;

    case COKVM_UI_STATUS_ERROR:
        gpio_pin_set_dt(&led_rgb_green, 0);
        gpio_pin_set_dt(&led_rgb_blue, 0);
        gpio_pin_toggle_dt(&led_rgb_red);
        k_msleep(500);
        break;

    case COKVM_UI_STATUS_NORMAL:
    default:
        gpio_pin_set_dt(&led_rgb_blue, 0);
        gpio_pin_set_dt(&led_rgb_red, 0);
        gpio_pin_set_dt(&led_rgb_green, 0);
        k_msleep(1000);
        break;
    }
}
