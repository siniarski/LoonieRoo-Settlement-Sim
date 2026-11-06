# An isometric view of a world exported from the TypeScript sim (harness/godotexport.harness.ts):
# ground as diamond tiles on one TileMapLayer, walls, water and furniture as raised blocks on a
# second, Y-sorted. Times each step and the frames, prints BENCH lines, and quits.
# Args after --: --size=128|1024 (which export), --frames=N (frames to time, default 240), --shot (save shot.png at frame 30).
extends Node2D

const TW := 64
const TH := 32
# Ground colours by sim GROUNDS index: grass dirt road forest bridge field paving boards tiles concrete.
const GROUND := [Color("3d5a34"), Color("5a4a36"), Color("4a4a4c"), Color("2e4a2a"), Color("6a5236"), Color("6a6a34"), Color("5c5c5a"), Color("5e4632"), Color("7a7e80"), Color("6a6c6c")]
const BLOCKS := {"#": Color("8a8f96"), "~": Color("2a4a6a"), "D": Color("8a5a2a"), "B": Color("6a5030"), "furniture": Color("7a6a50"), "tree": Color("2a5a28")}

var frames_left := 240
var frame_times: Array[float] = []
var started_ms := 0
var shot := false

func _ready() -> void:
	started_ms = Time.get_ticks_msec()
	var size := 128
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--size="): size = int(a.substr(7))
		if a.begins_with("--frames="): frames_left = int(a.substr(9))
		if a == "--shot": shot = true
	var t0 := Time.get_ticks_usec()
	var text := FileAccess.get_file_as_string("res://data/world-%d.json" % size)
	var w: Dictionary = JSON.parse_string(text)
	var t1 := Time.get_ticks_usec()
	var tileset := make_tileset()
	var ground := TileMapLayer.new()
	ground.tile_set = tileset
	add_child(ground)
	var blocks := TileMapLayer.new()
	blocks.tile_set = tileset
	blocks.y_sort_enabled = true
	add_child(blocks)
	var t2 := Time.get_ticks_usec()
	var tiles: String = w["tiles"]
	var grounds: PackedStringArray = (w["ground"] as String).split(",")
	var n := size * size
	for i in n:
		var x := i % size
		var y := i / size
		ground.set_cell(Vector2i(x, y), 0, Vector2i(int(grounds[i]), 0))
		var t := tiles[i]
		if t != ".": blocks.set_cell(Vector2i(x, y), 0, Vector2i(GROUND.size() + BLOCKS.keys().find(t), 0))
	var block_f := GROUND.size() + BLOCKS.keys().find("furniture")
	var block_t := GROUND.size() + BLOCKS.keys().find("tree")
	for f in w["furniture"]:
		blocks.set_cell(Vector2i(int(f[0]), int(f[1])), 0, Vector2i(block_t if f[2] == "tree" else block_f, 0))
	var t3 := Time.get_ticks_usec()
	var cam := Camera2D.new()
	var you: Array = w["survivors"][0] if w["survivors"].size() > 0 else [size / 2, size / 2]
	cam.position = ground.map_to_local(Vector2i(int(you[0]), int(you[1])))
	cam.zoom = Vector2(1, 1)
	add_child(cam)
	print("BENCH load_json_ms=%.1f build_tileset_ms=%.1f fill_cells_ms=%.1f cells=%d furniture=%d json_mb=%.1f" % [(t1 - t0) / 1000.0, (t2 - t1) / 1000.0, (t3 - t2) / 1000.0, n, w["furniture"].size(), text.length() / 1e6])

func _process(delta: float) -> void:
	if frame_times.is_empty() and started_ms > 0:
		print("BENCH first_frame_ms_after_ready=%d" % (Time.get_ticks_msec() - started_ms))
	frame_times.append(delta * 1000.0)
	if shot and frame_times.size() == 30:
		get_viewport().get_texture().get_image().save_png(ProjectSettings.globalize_path("res://shot.png"))
	# Pan a little so the view isn't static.
	var cam := get_viewport().get_camera_2d()
	if cam and not shot: cam.position += Vector2(2, 1)
	frames_left -= 1
	if frames_left <= 0:
		var sorted := frame_times.slice(5)
		sorted.sort()
		var mean := 0.0
		for v in sorted: mean += v
		mean /= max(1, sorted.size())
		print("BENCH frames=%d mean_frame_ms=%.2f p99_frame_ms=%.2f max_frame_ms=%.2f" % [sorted.size(), mean, sorted[int(sorted.size() * 0.99)], sorted[-1]])
		get_tree().quit()

## One atlas row: a flat diamond per ground colour, then a raised block per block colour.
func make_tileset() -> TileSet:
	var count := GROUND.size() + BLOCKS.size()
	var img := Image.create(TW * count, TH * 2, false, Image.FORMAT_RGBA8)
	for k in count:
		var raised := k >= GROUND.size()
		var c: Color = GROUND[k] if not raised else BLOCKS.values()[k - GROUND.size()]
		for py in TH * 2:
			for px in TW:
				# Diamond top face (offset up by TH for blocks), and for blocks the two side faces below it.
				var top_y := py - (0 if raised else TH)
				var dx := absf(px - TW / 2.0) / (TW / 2.0)
				var dy := absf(top_y - TH / 2.0) / (TH / 2.0)
				if dx + dy <= 1.0:
					img.set_pixel(k * TW + px, py, c)
				elif raised and py > TH / 2 and py < TH * 2 - absf(px - TW / 2.0) / 2.0 and dx <= 1.0 and top_y > TH / 2:
					img.set_pixel(k * TW + px, py, c.darkened(0.3 if px < TW / 2 else 0.45))
	var source := TileSetAtlasSource.new()
	source.texture = ImageTexture.create_from_image(img)
	source.texture_region_size = Vector2i(TW, TH * 2)
	for k in count:
		source.create_tile(Vector2i(k, 0))
		source.get_tile_data(Vector2i(k, 0), 0).texture_origin = Vector2i(0, TH / 2)
	var ts := TileSet.new()
	ts.tile_shape = TileSet.TILE_SHAPE_ISOMETRIC
	ts.tile_layout = TileSet.TILE_LAYOUT_DIAMOND_DOWN
	ts.tile_size = Vector2i(TW, TH)
	ts.add_source(source, 0)
	return ts
