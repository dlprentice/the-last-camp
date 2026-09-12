class_name UiTheme
extends RefCounted

## Small factory for the demo's UI look: warm accent, quiet greys, soft panels.

const ACCENT := Color(1.0, 0.80, 0.55)
const TEXT := Color(0.93, 0.93, 0.95)
const MUTED := Color(0.72, 0.74, 0.80, 0.9)
const PANEL := Color(0.03, 0.03, 0.045, 0.78)


static func label(text: String, size: int, color := TEXT,
		align := HORIZONTAL_ALIGNMENT_CENTER) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.add_theme_color_override("font_shadow_color", Color(0.0, 0.0, 0.0, 0.6))
	l.add_theme_constant_override("shadow_offset_x", 1)
	l.add_theme_constant_override("shadow_offset_y", 2)
	l.add_theme_constant_override("shadow_outline_size", 2)
	l.horizontal_alignment = align
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


static func panel(min_size: Vector2) -> PanelContainer:
	var p := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = PANEL
	style.set_corner_radius_all(10)
	style.set_border_width_all(1)
	style.border_color = Color(ACCENT, 0.22)
	style.set_content_margin_all(18)
	p.add_theme_stylebox_override("panel", style)
	p.custom_minimum_size = min_size
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return p


static func key_hint(parts: Array[String]) -> String:
	return "    ".join(parts)
