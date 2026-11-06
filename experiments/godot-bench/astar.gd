# The pathfinding benchmark in Godot: the same A* as harness/godotastar.harness.ts (4 ways, Manhattan, a binary
# heap of f * 2^21 + tile, stale entries skipped) over the same pairs, in GDScript; then the C++ plugin's copy
# (BenchCpp.astar_paths) and the engine's AStarGrid2D on the same grid. Prints BENCH lines and quits.
# Run: godot --headless --path experiments/godot-bench --script res://astar.gd -- --size=128|1024 [--skip-gd]
extends SceneTree

const SHIFT := 2097152

func _init() -> void:
	var size := 128
	var skip_gd := false
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--size="): size = int(a.substr(7))
		if a == "--skip-gd": skip_gd = true
	var w: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/world-%d.json" % size))
	var pairs_arr: Array = JSON.parse_string(FileAccess.get_file_as_string("res://data/pairs-%d.json" % size))["pairs"]
	var tiles: PackedByteArray = (w["tiles"] as String).to_ascii_buffer()
	var n := size * size
	var walk := PackedByteArray()
	walk.resize(n)
	for i in n:
		var t := tiles[i]
		walk[i] = 1 if (t == 46 or t == 68 or t == 66) else 0
	var pairs := PackedInt32Array()
	for p in pairs_arr: pairs.append(int(p))
	var count := pairs.size() / 2
	if not skip_gd:
		var g := PackedInt32Array()
		g.resize(n)
		var seen := PackedInt32Array()
		seen.resize(n)
		var heap := PackedInt64Array()
		heap.resize(n * 4)
		var stamp := [0]
		astar(walk, size, pairs[0], pairs[1], g, seen, heap, stamp) # warm up
		var t0 := Time.get_ticks_usec()
		var steps := 0
		for k in count: steps += astar(walk, size, pairs[2 * k], pairs[2 * k + 1], g, seen, heap, stamp)
		print("BENCH %d gd_astar paths=%d ms=%.1f steps=%d" % [size, count, (Time.get_ticks_usec() - t0) / 1000.0, steps])
	if ClassDB.class_exists("BenchCpp"):
		ClassDB.class_call_static("BenchCpp", "astar_paths", walk, size, pairs) # warm up
		var t1 := Time.get_ticks_usec()
		var csteps: int = ClassDB.class_call_static("BenchCpp", "astar_paths", walk, size, pairs)
		print("BENCH %d cpp_astar paths=%d ms=%.1f steps=%d" % [size, count, (Time.get_ticks_usec() - t1) / 1000.0, csteps])
	# The engine's A* (C++, its own heuristic and tie-breaking: same lengths, different paths).
	var tb := Time.get_ticks_usec()
	var grid := AStarGrid2D.new()
	grid.region = Rect2i(0, 0, size, size)
	grid.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
	grid.default_compute_heuristic = AStarGrid2D.HEURISTIC_MANHATTAN
	grid.default_estimate_heuristic = AStarGrid2D.HEURISTIC_MANHATTAN
	grid.update()
	for i in n:
		if walk[i] == 0: grid.set_point_solid(Vector2i(i % size, i / size))
	var setup_ms := (Time.get_ticks_usec() - tb) / 1000.0
	grid.get_id_path(Vector2i(pairs[0] % size, pairs[0] / size), Vector2i(pairs[1] % size, pairs[1] / size)) # warm up
	var t2 := Time.get_ticks_usec()
	var asteps := 0
	for k in count:
		var a := pairs[2 * k]
		var b := pairs[2 * k + 1]
		asteps += grid.get_id_path(Vector2i(a % size, a / size), Vector2i(b % size, b / size)).size() - 1
	print("BENCH %d astargrid2d paths=%d ms=%.1f steps=%d setup_ms=%.1f" % [size, count, (Time.get_ticks_usec() - t2) / 1000.0, asteps, setup_ms])
	quit()

func astar(walk: PackedByteArray, size: int, start: int, goal: int, g: PackedInt32Array, seen: PackedInt32Array, heap: PackedInt64Array, stamp_box: Array) -> int:
	stamp_box[0] += 1
	var stamp: int = stamp_box[0]
	var n := walk.size()
	var gx := goal % size
	var gy := goal / size
	var length := 0
	g[start] = 0
	seen[start] = stamp
	heap[0] = (absi(start % size - gx) + absi(start / size - gy)) * SHIFT + start
	length = 1
	while length > 0:
		# pop
		var key: int = heap[0]
		length -= 1
		if length > 0:
			heap[0] = heap[length]
			var k := 0
			while true:
				var l := 2 * k + 1
				var r := l + 1
				var m := k
				if l < length and heap[l] < heap[m]: m = l
				if r < length and heap[r] < heap[m]: m = r
				if m == k: break
				var t: int = heap[m]
				heap[m] = heap[k]
				heap[k] = t
				k = m
		var i: int = key % SHIFT
		var f: int = key / SHIFT
		var x: int = i % size
		if f > g[i] + absi(x - gx) + absi(i / size - gy): continue
		if i == goal: return g[i]
		var ng: int = g[i] + 1
		for d in 4:
			var j: int
			if d == 0: j = i - 1 if x > 0 else -1
			elif d == 1: j = i + 1 if x < size - 1 else -1
			elif d == 2: j = i - size
			else: j = i + size
			if j < 0 or j >= n or walk[j] == 0: continue
			if seen[j] == stamp and g[j] <= ng: continue
			seen[j] = stamp
			g[j] = ng
			# push
			var c := length
			length += 1
			heap[c] = (ng + absi(j % size - gx) + absi(j / size - gy)) * SHIFT + j
			while c > 0:
				var p := (c - 1) >> 1
				if heap[p] <= heap[c]: break
				var tt: int = heap[p]
				heap[p] = heap[c]
				heap[c] = tt
				c = p
	return -1
