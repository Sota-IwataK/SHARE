from setuptools import find_packages, setup

package_name = "amir_irt_mro"

setup(
    name=package_name,
    version="0.0.1",
    packages=find_packages(exclude=["test"]),
    data_files=[
        ("share/ament_index/resource_index/packages", ["resource/" + package_name]),
        ("share/" + package_name, ["package.xml"]),
    ],
    install_requires=["setuptools", "numpy", "ultralytics"],
    zip_safe=True,
    maintainer="YKAOR",
    maintainer_email="user@example.com",
    description="RealSense bottle pose detection node for AMIR IRT MRO.",
    license="MIT",
    tests_require=["pytest"],
    entry_points={
        "console_scripts": [
            "realsense_bottle_pose_node = amir_irt_mro.realsense_bottle_pose_node:main",
        ],
    },
)
