# The sim side of the benchmark, run headless: the same BFS floods as harness/godotexport.harness.ts,
# over the same exported 128 world, in GDScript. Prints a BENCH line and quits.
# Run: godot --headless --path experiments/godot-bench --script res://bench.gd
extends SceneTree

func _init() -> void:
	var w: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/world-128.json"))
	var size: int = w["size"]
	var tiles: PackedByteArray = (w["tiles"] as String).to_ascii_buffer()
	var n := size * size
	var dist := PackedInt32Array()
	dist.resize(n)
	var queue := PackedInt32Array()
	queue.resize(n)
	var starts: Array[int] = []
	var i := 0
	while i < n and starts.size() < 200:
		if tiles[i] == 46: starts.append(i)
		i += 81
	for k in 20: bfs(tiles, size, starts[0], dist, queue)
	var t0 := Time.get_ticks_usec()
	var reached := 0
	for s in starts: reached += bfs(tiles, size, s, dist, queue)
	print("BENCH gd_bfs_floods=%d ms=%.1f reached=%d" % [starts.size(), (Time.get_ticks_usec() - t0) / 1000.0, reached])
	quit()

func bfs(tiles: PackedByteArray, size: int, start: int, dist: PackedInt32Array, queue: PackedInt32Array) -> int:
	dist.fill(-1)
	var head := 0
	var tail := 0
	queue[tail] = start
	tail += 1
	dist[start] = 0
	while head < tail:
		var c := queue[head]
		head += 1
		var x := c % size
		var y := c / size
		var d := dist[c] + 1
		if x > 0: tail = visit(tiles, c - 1, d, dist, queue, tail)
		if x < size - 1: tail = visit(tiles, c + 1, d, dist, queue, tail)
		if y > 0: tail = visit(tiles, c - size, d, dist, queue, tail)
		if y < size - 1: tail = visit(tiles, c + size, d, dist, queue, tail)
	return tail

func visit(tiles: PackedByteArray, j: int, d: int, dist: PackedInt32Array, queue: PackedInt32Array, tail: int) -> int:
	if dist[j] != -1: return tail
	var t := tiles[j]
	if t != 46 and t != 68 and t != 66: return tail
	dist[j] = d
	queue[tail] = j
	return tail + 1
