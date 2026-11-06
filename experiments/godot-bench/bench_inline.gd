# bench.gd's BFS written the way GDScript likes it: no function call per neighbour, everything typed
# and local. Same 177 floods over the same world. Then the engine's own (C++) grid pathfinder,
# AStarGrid2D, for scale: 177 point-to-point paths (different work from a flood, so not a like-for-like).
# Run: godot --headless --path experiments/godot-bench --script res://bench_inline.gd
extends SceneTree

func _init() -> void:
	var w: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/world-128.json"))
	var size: int = w["size"]
	var tiles: PackedByteArray = (w["tiles"] as String).to_ascii_buffer()
	var n := size * size
	var walk := PackedByteArray()
	walk.resize(n)
	for i in n:
		var t := tiles[i]
		walk[i] = 1 if (t == 46 or t == 68 or t == 66) else 0
	var dist := PackedInt32Array()
	dist.resize(n)
	var queue := PackedInt32Array()
	queue.resize(n)
	var starts: Array[int] = []
	var s := 0
	while s < n and starts.size() < 200:
		if tiles[s] == 46: starts.append(s)
		s += 81
	var t0 := Time.get_ticks_usec()
	var reached := 0
	var best_start := starts[0]
	var best_tail := 0
	for start in starts:
		dist.fill(-1)
		var head := 0
		var tail := 1
		queue[0] = start
		dist[start] = 0
		while head < tail:
			var c: int = queue[head]
			head += 1
			var x: int = c % size
			var d: int = dist[c] + 1
			var j: int = c - 1
			if x > 0 and dist[j] == -1 and walk[j] == 1:
				dist[j] = d
				queue[tail] = j
				tail += 1
			j = c + 1
			if x < size - 1 and dist[j] == -1 and walk[j] == 1:
				dist[j] = d
				queue[tail] = j
				tail += 1
			j = c - size
			if j >= 0 and dist[j] == -1 and walk[j] == 1:
				dist[j] = d
				queue[tail] = j
				tail += 1
			j = c + size
			if j < n and dist[j] == -1 and walk[j] == 1:
				dist[j] = d
				queue[tail] = j
				tail += 1
		reached += tail
		if tail > best_tail:
			best_tail = tail
			best_start = start
	print("BENCH gd_inline_bfs_floods=%d ms=%.1f reached=%d" % [starts.size(), (Time.get_ticks_usec() - t0) / 1000.0, reached])
	# The engine's C++ A* on the same grid: a path from each start to one far tile (none from starts walled off from it).
	var astar := AStarGrid2D.new()
	astar.region = Rect2i(0, 0, size, size)
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
	astar.update()
	for i in n:
		if walk[i] == 0: astar.set_point_solid(Vector2i(i % size, i / size))
	# The goal: the tile furthest from the start that reaches most of the map.
	dist.fill(-1)
	var head2 := 0
	var tail2 := 1
	queue[0] = best_start
	dist[best_start] = 0
	while head2 < tail2:
		var c2: int = queue[head2]
		head2 += 1
		for j2 in [c2 - 1, c2 + 1, c2 - size, c2 + size]:
			if j2 >= 0 and j2 < n and dist[j2] == -1 and walk[j2] == 1 and absi(j2 % size - c2 % size) <= 1:
				dist[j2] = dist[c2] + 1
				queue[tail2] = j2
				tail2 += 1
	var goal: int = queue[tail2 - 1]
	var found := 0
	var t1 := Time.get_ticks_usec()
	var steps := 0
	for start in starts:
		var path := astar.get_id_path(Vector2i(start % size, start / size), Vector2i(goal % size, goal / size))
		steps += path.size()
		if path.size() > 0: found += 1
	print("BENCH astargrid2d_paths=%d found=%d ms=%.1f steps=%d" % [starts.size(), found, (Time.get_ticks_usec() - t1) / 1000.0, steps])
	quit()
