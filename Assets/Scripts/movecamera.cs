
using UnityEngine;

/// <summary>
/// GameビューにてSceneビューのようなカメラの動きをマウス操作によって実現する
/// </summary>

public class movecamera : MonoBehaviour
{



    [SerializeField, Range(0.1f, 10f)]
    private float rotateSpeed = 0.3f;
    public float speed = 3.0f;

    private Vector3 preMousePos;

    private void Update()
    {
        MouseUpdate();
        MoveUpdate();
        return;
    }
    private void MoveUpdate()
    {

        if (Input.GetKey("w"))
        {
            transform.position += transform.forward * speed * Time.deltaTime;
        }
        if (Input.GetKey("s"))
        {
            transform.position -= transform.forward * speed * Time.deltaTime;
        }
        if (Input.GetKey("a"))
        {
            transform.position -= transform.right * speed * Time.deltaTime;
        }
        if (Input.GetKey("d"))
        {
            transform.position += transform.right * speed * Time.deltaTime;
        }

        if (Input.GetKey("q"))
        {
            transform.position += transform.up * speed * Time.deltaTime;
        }
        if (Input.GetKey("e"))
        {
            transform.position -= transform.up * speed * Time.deltaTime;
        }

    }


    private void MouseUpdate()
    {
        float scrollWheel = Input.GetAxis("Mouse ScrollWheel");
        //if (scrollWheel != 0.0f)
        //    MouseWheel(scrollWheel);

        if (Input.GetMouseButtonDown(0) ||
           Input.GetMouseButtonDown(1) ||
           Input.GetMouseButtonDown(2))
            preMousePos = Input.mousePosition;

        MouseDrag(Input.mousePosition);
    }



    private void MouseDrag(Vector3 mousePos)
    {
        Vector3 diff = mousePos - preMousePos;

        if (diff.magnitude < Vector3.kEpsilon)
            return;


        if (Input.GetMouseButton(1))
            CameraRotate(new Vector2(-diff.y, diff.x) * rotateSpeed);

        preMousePos = mousePos;
    }

    public void CameraRotate(Vector2 angle)
    {
        transform.RotateAround(transform.position, transform.right, angle.x);
        transform.RotateAround(transform.position, Vector3.up, angle.y);
    }
}



