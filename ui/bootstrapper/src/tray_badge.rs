#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BadgeStyle {
    Template,
    Colour,
}

const DISC_DIAMETER: f64 = 0.42;
const GAP: f64 = 1.0 / 16.0;
const SUBSAMPLES: u32 = 4;
const BADGE_GREEN: [u8; 3] = [0x16, 0xa3, 0x4a];
const ARROW_HALF_STROKE: f64 = 0.17;
const ARROW_SEGMENTS: [((f64, f64), (f64, f64)); 3] = [
    ((0.0, 0.5), (0.0, -0.5)),
    ((-0.45, -0.05), (0.0, -0.5)),
    ((0.45, -0.05), (0.0, -0.5)),
];

#[derive(Clone, Copy, PartialEq)]
enum Region {
    Outside,
    Gap,
    Disc,
    Arrow,
}

struct Badge {
    centre_x: f64,
    centre_y: f64,
    radius: f64,
    outer_radius: f64,
}

impl Badge {
    fn for_icon(width: u32, height: u32) -> Self {
        let size = f64::from(width.min(height));
        let radius = size * DISC_DIAMETER / 2.0;
        Self {
            centre_x: f64::from(width) - radius,
            centre_y: radius,
            radius,
            outer_radius: radius + size * GAP,
        }
    }

    fn region(&self, x: f64, y: f64) -> Region {
        let dx = x - self.centre_x;
        let dy = y - self.centre_y;
        let distance = dx.hypot(dy);
        if distance > self.outer_radius {
            return Region::Outside;
        }
        if distance > self.radius {
            return Region::Gap;
        }
        let (ux, uy) = (dx / self.radius, dy / self.radius);
        let on_arrow = ARROW_SEGMENTS
            .iter()
            .any(|&(a, b)| distance_to_segment((ux, uy), a, b) <= ARROW_HALF_STROKE);
        if on_arrow {
            Region::Arrow
        } else {
            Region::Disc
        }
    }
}

fn distance_to_segment(p: (f64, f64), a: (f64, f64), b: (f64, f64)) -> f64 {
    let (abx, aby) = (b.0 - a.0, b.1 - a.1);
    let (apx, apy) = (p.0 - a.0, p.1 - a.1);
    let t = ((apx * abx + apy * aby) / (abx * abx + aby * aby)).clamp(0.0, 1.0);
    (apx - t * abx).hypot(apy - t * aby)
}

fn region_colour(region: Region, style: BadgeStyle, original: [u8; 4]) -> [u8; 4] {
    match (region, style) {
        (Region::Outside, _) => original,
        (Region::Gap, _) | (Region::Arrow, BadgeStyle::Template) => [0, 0, 0, 0],
        (Region::Disc, BadgeStyle::Template) => [0, 0, 0, 255],
        (Region::Disc, BadgeStyle::Colour) => [BADGE_GREEN[0], BADGE_GREEN[1], BADGE_GREEN[2], 255],
        (Region::Arrow, BadgeStyle::Colour) => [255, 255, 255, 255],
    }
}

pub fn with_update_badge(rgba: &[u8], width: u32, height: u32, style: BadgeStyle) -> Vec<u8> {
    let mut output = rgba.to_vec();
    let badge = Badge::for_icon(width, height);
    let step = 1.0 / f64::from(SUBSAMPLES);
    let samples = f64::from(SUBSAMPLES * SUBSAMPLES);

    for y in 0..height {
        for x in 0..width {
            let index = ((y * width + x) * 4) as usize;
            let original = [
                rgba[index],
                rgba[index + 1],
                rgba[index + 2],
                rgba[index + 3],
            ];
            let mut premultiplied = [0.0_f64; 3];
            let mut alpha = 0.0_f64;
            let mut touched = false;
            for sy in 0..SUBSAMPLES {
                for sx in 0..SUBSAMPLES {
                    let px = f64::from(x) + (f64::from(sx) + 0.5) * step;
                    let py = f64::from(y) + (f64::from(sy) + 0.5) * step;
                    let region = badge.region(px, py);
                    touched |= region != Region::Outside;
                    let colour = region_colour(region, style, original);
                    let a = f64::from(colour[3]) / 255.0;
                    for (channel, value) in premultiplied.iter_mut().zip(colour) {
                        *channel += f64::from(value) * a;
                    }
                    alpha += a;
                }
            }
            if !touched {
                continue;
            }
            if alpha > 0.0 {
                for (offset, channel) in premultiplied.iter().enumerate() {
                    output[index + offset] = (channel / alpha).round().clamp(0.0, 255.0) as u8;
                }
            } else {
                output[index..index + 3].fill(0);
            }
            output[index + 3] = (alpha / samples * 255.0).round() as u8;
        }
    }
    output
}

#[cfg(test)]
mod tests {
    use super::*;

    const SIZES: [u32; 2] = [32, 44];

    fn opaque_square(size: u32, colour: [u8; 4]) -> Vec<u8> {
        colour.repeat((size * size) as usize)
    }

    fn pixel(rgba: &[u8], width: u32, x: u32, y: u32) -> [u8; 4] {
        let i = ((y * width + x) * 4) as usize;
        [rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]]
    }

    fn disc_centre(size: u32) -> (u32, u32) {
        let radius = f64::from(size) * DISC_DIAMETER / 2.0;
        ((f64::from(size) - radius) as u32, radius as u32)
    }

    #[test]
    fn the_badge_stays_inside_the_top_right_quadrant() {
        for size in SIZES {
            for style in [BadgeStyle::Template, BadgeStyle::Colour] {
                let base = opaque_square(size, [10, 20, 30, 255]);
                let badged = with_update_badge(&base, size, size, style);
                for y in 0..size {
                    for x in 0..size {
                        if x < size / 2 || y >= size / 2 {
                            assert_eq!(pixel(&badged, size, x, y), pixel(&base, size, x, y));
                        }
                    }
                }
                assert_ne!(badged, base);
            }
        }
    }

    #[test]
    fn a_transparent_gap_separates_the_badge_from_an_opaque_glyph() {
        for size in SIZES {
            let base = opaque_square(size, [10, 20, 30, 255]);
            let badged = with_update_badge(&base, size, size, BadgeStyle::Colour);
            let (_, cy) = disc_centre(size);
            let diameter = f64::from(size) * DISC_DIAMETER;
            let gap_x = (f64::from(size) - diameter - f64::from(size) * GAP / 2.0) as u32;
            assert_eq!(pixel(&badged, size, gap_x, cy)[3], 0);
        }
    }

    #[test]
    fn the_colour_badge_is_a_green_disc_with_a_white_arrow() {
        for size in SIZES {
            let base = opaque_square(size, [10, 20, 30, 255]);
            let badged = with_update_badge(&base, size, size, BadgeStyle::Colour);
            let (cx, cy) = disc_centre(size);
            assert_eq!(pixel(&badged, size, cx, cy), [255, 255, 255, 255]);
            let radius = f64::from(size) * DISC_DIAMETER / 2.0;
            let beside_shaft = (f64::from(cx) + radius * 0.6) as u32;
            let below_chevron = (f64::from(cy) + radius * 0.35) as u32;
            assert_eq!(
                pixel(&badged, size, beside_shaft, below_chevron),
                [BADGE_GREEN[0], BADGE_GREEN[1], BADGE_GREEN[2], 255]
            );
        }
    }

    #[test]
    fn the_template_badge_knocks_the_arrow_out_of_a_black_disc() {
        for size in SIZES {
            let base = vec![0; (size * size * 4) as usize];
            let badged = with_update_badge(&base, size, size, BadgeStyle::Template);
            let (cx, cy) = disc_centre(size);
            assert_eq!(pixel(&badged, size, cx, cy)[3], 0);
            let radius = f64::from(size) * DISC_DIAMETER / 2.0;
            let beside_shaft = (f64::from(cx) + radius * 0.6) as u32;
            let below_chevron = (f64::from(cy) + radius * 0.35) as u32;
            assert_eq!(
                pixel(&badged, size, beside_shaft, below_chevron),
                [0, 0, 0, 255]
            );
        }
    }

    #[test]
    fn the_template_badge_keeps_a_template_icon_pure_black() {
        let base = tauri::image::Image::from_bytes(include_bytes!("../icons/tray-icon-macos.png"))
            .expect("the macOS tray icon decodes");
        let badged = with_update_badge(
            base.rgba(),
            base.width(),
            base.height(),
            BadgeStyle::Template,
        );
        assert!(badged.chunks(4).all(|px| px[..3] == [0, 0, 0]));
    }
}
