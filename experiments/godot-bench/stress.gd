# The Godot half of the rendering benchmark (experiments/render-bench): the stress scenario's map drawn
# isometric as main.gd draws it, and every recorded mover (harness/renderbench.harness.ts) replayed on it
# as a Sprite2D, 10 ticks a second, blended between ticks as the browser does. Times the frames.
# Args after --: --data=<dir with <scenario>.world.json and .movers.bin> --scenario=city-1024 --zoom=<camera zoom>
#   --seconds=N (timed, after 2 s settling; default 8) --shot=<png path>
# Godot zoom 0.75 shows as many tiles as the browser at zoom 1 (a 64x32 diamond against a 24 px square),
# and 0.1875 as many as the browser's 0.25.
extends Node2D

const SUB := 256.0
const TICKS_PER_SECOND := 10.0

var helper = preload("res://main.gd").new()
var track := PackedInt32Array()
var tick_at := PackedInt32Array() # where each tick's records start in track
var ticks := 0
var sprites: Array[Sprite2D] = []
var movers_root := Node2D.new()
var origin := Vector2.ZERO
var clock := 0.0
var settle := 2.0
var seconds := 8.0
var shot := ""
var frame_ms: Array[float] = []
var update_ms: Array[float] = []
var render_cpu: Array[float] = []
var render_gpu: Array[float] = []
var viewport_rid: RID

func _ready() -> void:
	var data := ""
	var scenario := "city-1024"
	var zoom := 0.75
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--data="): data = a.substr(7)
		if a.begins_with("--scenario="): scenario = a.substr(11)
		if a.begins_with("--zoom="): zoom = float(a.substr(7))
		if a.begins_with("--seconds="): seconds = float(a.substr(10))
		if a.begins_with("--shot="): shot = a.substr(7)
	var t0 := Time.get_ticks_usec()
	var w: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("%s/%s.world.json" % [data, scenario]))
	var size: int = w["size"]
	var tileset: TileSet = helper.make_tileset()
	var ground := TileMapLayer.new()
	ground.tile_set = tileset
	add_child(ground)
	var blocks := TileMapLayer.new()
	blocks.tile_set = tileset
	blocks.y_sort_enabled = true
	add_child(blocks)
	var tiles: String = w["tiles"]
	var grounds: PackedStringArray = (w["ground"] as String).split(",")
	var keys: Array = helper.BLOCKS.keys()
	var nground: int = helper.GROUND.size()
	for i in size * size:
		var cell := Vector2i(i % size, i / size)
		ground.set_cell(cell, 0, Vector2i(int(grounds[i]), 0))
		var t := tiles[i]
		if t != ".": blocks.set_cell(cell, 0, Vector2i(nground + keys.find(t), 0))
	for f in w["furniture"]:
		blocks.set_cell(Vector2i(int(f[0]), int(f[1])), 0, Vector2i(nground + keys.find("tree" if f[2] == "tree" else "furniture"), 0))
	origin = ground.map_to_local(Vector2i(0, 0))
	# The movers' track.
	track = FileAccess.get_file_as_bytes("%s/%s.movers.bin" % [data, scenario]).to_int32_array()
	ticks = track[0]
	var at := 1
	var most := 0
	for t in ticks:
		tick_at.append(at)
		most = max(most, track[at])
		at += 1 + track[at] * 3
	# One sprite per mover (the most there ever are), a dot per kind as the browser draws them.
	var dots := [dot(Color("e8e0d0")), dot(Color("8a3a32")), dot(Color("b07a46"))]
	movers_root.y_sort_enabled = true
	add_child(movers_root)
	for k in most:
		var s := Sprite2D.new()
		s.texture = dots[1]
		movers_root.add_child(s)
		sprites.append(s)
	var cam := Camera2D.new()
	var home: Array = w["survivors"][0]
	cam.position = ground.map_to_local(Vector2i(int(home[0]), int(home[1])))
	cam.zoom = Vector2(zoom, zoom)
	add_child(cam)
	viewport_rid = get_viewport().get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(viewport_rid, true)
	print("BENCH setup_ms=%.0f size=%d movers=%d ticks=%d zoom=%.4f" % [(Time.get_ticks_usec() - t0) / 1000.0, size, most, ticks, zoom])

func dot(c: Color) -> ImageTexture:
	var img := Image.create(16, 16, false, Image.FORMAT_RGBA8)
	for y in 16:
		for x in 16:
			if Vector2(x - 7.5, y - 7.5).length() <= 7.0: img.set_pixel(x, y, c)
	return ImageTexture.create_from_image(img)

func iso(fx: float, fy: float) -> Vector2:
	return origin + Vector2((fx - fy) * helper.TW / 2.0, (fx + fy) * helper.TH / 2.0)

func _process(delta: float) -> void:
	clock += delta
	var u0 := Time.get_ticks_usec()
	var tf := clock * TICKS_PER_SECOND
	var t := int(tf) % (ticks - 1)
	var alpha := tf - floorf(tf)
	var a: int = tick_at[t]
	var b: int = tick_at[t + 1]
	var na: int = track[a]
	var nb: int = track[b]
	for k in sprites.size():
		var s := sprites[k]
		if k >= na:
			s.visible = false
			continue
		s.visible = true
		var i := a + 1 + k * 3
		var x: float = track[i + 1]
		var y: float = track[i + 2]
		if k < nb:
			var j := b + 1 + k * 3
			x += (track[j + 1] - x) * alpha
			y += (track[j + 2] - y) * alpha
		s.position = iso(x / SUB - 0.5, y / SUB - 0.5)
	if clock < settle: return
	update_ms.append((Time.get_ticks_usec() - u0) / 1000.0)
	frame_ms.append(delta * 1000.0)
	render_cpu.append(RenderingServer.viewport_get_measured_render_time_cpu(viewport_rid) + RenderingServer.get_frame_setup_time_cpu())
	render_gpu.append(RenderingServer.viewport_get_measured_render_time_gpu(viewport_rid))
	if shot != "" and frame_ms.size() == 60:
		get_viewport().get_texture().get_image().save_png(shot)
	if clock >= settle + seconds:
		print("BENCH frames=%d fps=%.1f frame %s | movers_update %s | render_cpu %s | render_gpu %s" % [frame_ms.size(), frame_ms.size() / seconds, stats(frame_ms), stats(update_ms), stats(render_cpu), stats(render_gpu)])
		get_tree().quit()

func stats(xs: Array[float]) -> String:
	var a := xs.duplicate()
	a.sort()
	var mean := 0.0
	for v in a: mean += v
	mean /= max(1, a.size())
	return "mean=%.2f p50=%.2f p99=%.2f max=%.2f" % [mean, a[a.size() / 2], a[int(a.size() * 0.99)], a[-1]]
