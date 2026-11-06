#pragma once

#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/variant/packed_byte_array.hpp>
#include <godot_cpp/variant/packed_int32_array.hpp>

class BenchCpp : public godot::RefCounted {
	GDCLASS(BenchCpp, godot::RefCounted)

protected:
	static void _bind_methods();

public:
	static int64_t bfs_floods(const godot::PackedByteArray &walk, int64_t size, const godot::PackedInt32Array &starts);
};
