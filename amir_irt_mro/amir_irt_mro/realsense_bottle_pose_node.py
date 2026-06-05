import math
from typing import List, Optional, Tuple

import numpy as np
import rclpy
from cv_bridge import CvBridge, CvBridgeError
from geometry_msgs.msg import Pose, PoseArray, PoseStamped
from rclpy.node import Node
from sensor_msgs.msg import CameraInfo, Image
from ultralytics import YOLO


class RealSenseBottlePoseNode(Node):
    def __init__(self) -> None:
        super().__init__("realsense_bottle_pose_node")

        self.declare_parameter("color_image_topic", "/camera/camera/color/image_raw")
        self.declare_parameter("depth_image_topic", "/camera/camera/aligned_depth_to_color/image_raw")
        self.declare_parameter("camera_info_topic", "/camera/camera/color/camera_info")
        self.declare_parameter("output_topic", "/detected_bottle_pose")
        self.declare_parameter("frame_id", "camera_color_optical_frame")
        self.declare_parameter("model_path", "yolov8n.pt")
        self.declare_parameter("confidence_threshold", 0.25)
        self.declare_parameter("depth_window_size", 5)
        self.declare_parameter("min_depth_m", 0.05)
        self.declare_parameter("max_depth_m", 1.2)
        self.declare_parameter("max_bottles", 5)
        self.declare_parameter("publish_pose_array", True)
        self.declare_parameter("pose_array_topic", "/detected_bottle_poses")

        self.color_image_topic = self.get_parameter("color_image_topic").value
        self.depth_image_topic = self.get_parameter("depth_image_topic").value
        self.camera_info_topic = self.get_parameter("camera_info_topic").value
        self.output_topic = self.get_parameter("output_topic").value
        self.frame_id = self.get_parameter("frame_id").value
        self.model_path = self.get_parameter("model_path").value
        self.confidence_threshold = float(self.get_parameter("confidence_threshold").value)
        self.depth_window_size = int(self.get_parameter("depth_window_size").value)
        self.min_depth_m = float(self.get_parameter("min_depth_m").value)
        self.max_depth_m = float(self.get_parameter("max_depth_m").value)
        self.max_bottles = int(self.get_parameter("max_bottles").value)
        self.publish_pose_array = bool(self.get_parameter("publish_pose_array").value)
        self.pose_array_topic = self.get_parameter("pose_array_topic").value
        if self.depth_window_size < 1:
            self.depth_window_size = 1
        if self.depth_window_size % 2 == 0:
            self.depth_window_size += 1
        if self.max_bottles < 1:
            self.max_bottles = 1
        if self.min_depth_m < 0.0:
            self.min_depth_m = 0.0
        if self.max_depth_m < self.min_depth_m:
            self.get_logger().warn(
                "max_depth_m is smaller than min_depth_m; swapping depth range values."
            )
            self.min_depth_m, self.max_depth_m = self.max_depth_m, self.min_depth_m

        self.bridge = CvBridge()
        self.model = YOLO(self.model_path)
        self.latest_depth_msg: Optional[Image] = None
        self.latest_depth_image: Optional[np.ndarray] = None
        self.latest_camera_info: Optional[CameraInfo] = None

        self.pose_pub = self.create_publisher(PoseStamped, self.output_topic, 10)
        self.pose_array_pub = self.create_publisher(PoseArray, self.pose_array_topic, 10)
        self.create_subscription(Image, self.color_image_topic, self.color_callback, 10)
        self.create_subscription(Image, self.depth_image_topic, self.depth_callback, 10)
        self.create_subscription(CameraInfo, self.camera_info_topic, self.camera_info_callback, 10)

        self.get_logger().info(
            "RealSense bottle pose node started: "
            f"color={self.color_image_topic}, depth={self.depth_image_topic}, "
            f"camera_info={self.camera_info_topic}, output={self.output_topic}, "
            f"pose_array={self.pose_array_topic}, frame_id={self.frame_id}, "
            f"model={self.model_path}, depth_range=[{self.min_depth_m:.3f}, "
            f"{self.max_depth_m:.3f}] m, max_bottles={self.max_bottles}, "
            f"publish_pose_array={self.publish_pose_array}"
        )

    def depth_callback(self, msg: Image) -> None:
        try:
            self.latest_depth_image = self.bridge.imgmsg_to_cv2(msg, desired_encoding="passthrough")
            self.latest_depth_msg = msg
        except CvBridgeError as exc:
            self.get_logger().warn(f"Depth image conversion failed: {exc}")

    def camera_info_callback(self, msg: CameraInfo) -> None:
        self.latest_camera_info = msg

    def color_callback(self, msg: Image) -> None:
        if self.latest_depth_image is None or self.latest_depth_msg is None:
            self.get_logger().debug("Skipping detection: depth image is not available yet.")
            return
        if self.latest_camera_info is None:
            self.get_logger().debug("Skipping detection: camera_info is not available yet.")
            return

        try:
            color_image = self.bridge.imgmsg_to_cv2(msg, desired_encoding="bgr8")
        except CvBridgeError as exc:
            self.get_logger().warn(f"Color image conversion failed: {exc}")
            return

        detections = self.detect_bottles(color_image)
        if not detections:
            return

        valid_detections = []
        for confidence, center_u, center_v in detections:
            depth_m = self.get_median_depth_m(center_u, center_v)
            if not self.is_depth_in_range(depth_m):
                self.get_logger().info(
                    "bottle skipped by depth range: "
                    f"confidence={confidence:.3f}, "
                    f"depth_m={self.format_depth(depth_m)}, "
                    f"pixel=({center_u}, {center_v})"
                )
                continue

            xyz = self.deproject_pixel_to_point(
                center_u,
                center_v,
                depth_m,
                self.latest_camera_info,
            )
            if xyz is None:
                continue

            valid_detections.append((confidence, depth_m, center_u, center_v, xyz))

        if not valid_detections:
            return

        valid_detections.sort(key=lambda item: item[0], reverse=True)
        limited_detections = valid_detections[: self.max_bottles]

        confidence, depth_m, center_u, center_v, xyz = limited_detections[0]
        pose_msg = PoseStamped()
        pose_msg.header.stamp = msg.header.stamp
        pose_msg.header.frame_id = self.frame_id
        pose_msg.pose = self.make_pose(xyz)
        self.pose_pub.publish(pose_msg)

        if self.publish_pose_array:
            pose_array_msg = PoseArray()
            pose_array_msg.header.stamp = msg.header.stamp
            pose_array_msg.header.frame_id = self.frame_id
            pose_array_msg.poses = [
                self.make_pose(valid_xyz)
                for _, _, _, _, valid_xyz in limited_detections
            ]
            self.pose_array_pub.publish(pose_array_msg)

        x, y, z = xyz
        self.get_logger().info(
            f"bottle conf={confidence:.3f}, depth={depth_m:.3f} m, "
            f"pixel=({center_u}, {center_v}), xyz=({x:.3f}, {y:.3f}, {z:.3f}), "
            f"valid_count={len(limited_detections)}"
        )

    def detect_bottles(self, color_image: np.ndarray) -> List[Tuple[float, int, int]]:
        results = self.model(color_image, verbose=False)
        if not results:
            return []

        detections: List[Tuple[float, int, int]] = []
        result = results[0]
        names = result.names if hasattr(result, "names") else self.model.names

        for box in result.boxes:
            confidence = float(box.conf[0].item())
            if confidence < self.confidence_threshold:
                continue

            class_id = int(box.cls[0].item())
            class_name = names.get(class_id, str(class_id)) if isinstance(names, dict) else str(class_id)
            if class_name != "bottle":
                continue

            x1, y1, x2, y2 = [float(value.item()) for value in box.xyxy[0]]
            center_u = int(round((x1 + x2) * 0.5))
            center_v = int(round((y1 + y2) * 0.5))
            detections.append((confidence, center_u, center_v))

        detections.sort(key=lambda item: item[0], reverse=True)
        return detections

    def get_median_depth_m(self, center_u: int, center_v: int) -> Optional[float]:
        if self.latest_depth_image is None or self.latest_depth_msg is None:
            return None

        depth = self.latest_depth_image
        if depth.ndim != 2:
            self.get_logger().warn(f"Unsupported depth image shape: {depth.shape}")
            return None

        height, width = depth.shape[:2]
        if center_u < 0 or center_u >= width or center_v < 0 or center_v >= height:
            self.get_logger().warn(
                f"Depth lookup outside image: u={center_u}, v={center_v}, size={width}x{height}"
            )
            return None

        half = self.depth_window_size // 2
        u_min = max(0, center_u - half)
        u_max = min(width, center_u + half + 1)
        v_min = max(0, center_v - half)
        v_max = min(height, center_v + half + 1)

        window = depth[v_min:v_max, u_min:u_max].astype(np.float32)
        valid_depths = window[np.isfinite(window) & (window > 0.0)]
        if valid_depths.size == 0:
            return None

        median_depth = float(np.median(valid_depths))
        encoding = self.latest_depth_msg.encoding.upper()
        if "16UC1" in encoding or depth.dtype == np.uint16:
            median_depth *= 0.001

        if not math.isfinite(median_depth) or median_depth <= 0.0:
            return None

        return median_depth

    def is_depth_in_range(self, depth_m: Optional[float]) -> bool:
        if depth_m is None:
            return False
        if not math.isfinite(depth_m) or depth_m <= 0.0:
            return False
        return self.min_depth_m <= depth_m <= self.max_depth_m

    @staticmethod
    def format_depth(depth_m: Optional[float]) -> str:
        if depth_m is None:
            return "None"
        if not math.isfinite(depth_m):
            return str(depth_m)
        return f"{depth_m:.3f}"

    @staticmethod
    def make_pose(xyz: Tuple[float, float, float]) -> Pose:
        x, y, z = xyz
        pose = Pose()
        pose.position.x = x
        pose.position.y = y
        pose.position.z = z
        pose.orientation.x = 0.0
        pose.orientation.y = 0.0
        pose.orientation.z = 0.0
        pose.orientation.w = 1.0
        return pose

    @staticmethod
    def deproject_pixel_to_point(
        u: int,
        v: int,
        depth_m: float,
        camera_info: CameraInfo,
    ) -> Optional[Tuple[float, float, float]]:
        fx = float(camera_info.k[0])
        fy = float(camera_info.k[4])
        cx = float(camera_info.k[2])
        cy = float(camera_info.k[5])
        if fx == 0.0 or fy == 0.0:
            return None

        x = (float(u) - cx) * depth_m / fx
        y = (float(v) - cy) * depth_m / fy
        z = depth_m
        return x, y, z


def main(args=None) -> None:
    rclpy.init(args=args)
    node = RealSenseBottlePoseNode()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
