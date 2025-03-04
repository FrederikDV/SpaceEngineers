#ifndef FRAME_CONSTANTS__
#define FRAME_CONSTANTS__
#include <common.h>
#include <Math/math.h>

struct FrameConstants {
	//
	matrix 	view_projection_matrix;
	matrix	view_matrix;
	matrix	projection_matrix;
	matrix 	inv_view_matrix;
	matrix  inv_proj_matrix;
	matrix 	inv_view_proj_matrix;

	matrix 	view_projection_matrix_world;
	float4	world_offset;

	float2	resolution;

	float 	time;
	float 	timedelta;
	//

	float4 	terrain_texture_distances;

	float2 	terrain_material_transition;

	uint 	tiles_num;
	uint 	tiles_x;

	//
	float4 	foliage_clipping_scaling;
	//
	float3 	wind_vec;
	float 	tau;
	//
	float 	backlight_mult;
	float 	env_mult;
	float 	contrast;
	float	brightness;

	float 	middle_grey;
	float 	luminance_exposure;
	float 	bloom_exposure;
	float 	bloom_mult;

	float 	middle_grey_curve_sharpness;
	float 	middle_grey_0;
	float 	blue_shift_rapidness;
	float 	blue_shift_scale;

	float 	fog_density;
	float 	fog_mult;
	float 	fog_offset;
	uint	fog_color;

	float3  directionalLightVec;
	float 	skyboxBlend;

	float3 	directionalLightColor;
	float 	forwardPassAmbient;

    float3  additionalSunColor;
    float   additionalSunIntensity;

    float4  additionalSunDirection[MAX_ADDITIONAL_SUNS];

    int     additionalSunsInUse;
    float3  _padding1;

	float 	tonemapping_A;
	float 	tonemapping_B;
	float 	tonemapping_C;
	float 	tonemapping_D;

	float 	tonemapping_E;
	float 	tonemapping_F;
	float 	logLumThreshold;
	float 	debug_voxel_lod;

	// up to 8 lod levels + 16 massive levels
	float4 voxel_lod_range[12];

	float skyboxBrightness;
	float shadowFadeout;
	float2 _padding;

	float EnableVoxelAo;
	float VoxelAoMin;
	float VoxelAoMax;
	float VoxelAoOffset;

	matrix background_orientation;
};

cbuffer Frame : register( MERGE(b,FRAME_SLOT) )
{
	FrameConstants frame_;
};

float2 screen_to_uv(float2 screencoord)
{
	const float2 invres = 1 / frame_.resolution;
	return screencoord * invres;
}

float3 get_camera_position()
{
	return 0;
}

float3 ReconstructWorldPosition(float hwDepth, float2 uv) {
	const float ray_x = 1./frame_.projection_matrix._11;
	const float ray_y = 1./frame_.projection_matrix._22;
	float3 screen_ray = float3(lerp( -ray_x, ray_x, uv.x ), -lerp( -ray_y, ray_y, uv.y ), -1.);
	float depth = -linearize_depth(hwDepth, frame_.projection_matrix);
	float3 viewDirection = mul(screen_ray, transpose((float3x3)frame_.view_matrix));

    return depth * viewDirection;
}

float2 get_voxel_lod_range(uint lod, int isMassive)
{
	lod = min(lod + 8 * isMassive, 8 + 16 - 1);
	return (lod % 2) ? frame_.voxel_lod_range[lod / 2].zw : frame_.voxel_lod_range[lod / 2].xy;
}

#endif
