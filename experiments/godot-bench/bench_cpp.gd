# The C++ plugin's half of the sim benchmark: the same 177 BFS floods over the same 128 world, in one
# call into BenchCpp (cpp/src/bench.cpp). Prints a BENCH line and quits.
# Run: godot --headless --path experiments/godot-bench --script res://bench_cpp.gd
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
	var starts := PackedInt32Array()
	var s := 0
	while s < n and starts.size() < 200:
		if tiles[s] == 46: starts.append(s)
		s += 81
	ClassDB.class_call_static("BenchCpp", "bfs_floods", walk, size, starts) # warm up
	var t0 := Time.get_ticks_usec()
	var reached: int = ClassDB.class_call_static("BenchCpp", "bfs_floods", walk, size, starts)
	print("BENCH cpp_bfs_floods=%d ms=%.1f reached=%d" % [starts.size(), (Time.get_ticks_usec() - t0) / 1000.0, reached])
	quit()
