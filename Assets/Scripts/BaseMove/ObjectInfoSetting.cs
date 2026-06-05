using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectInfoSetting : MonoBehaviour
{
    public Vector3 point;
    /*
    public GameObject border;
    public GameObject origin;
    public int count;

    public Vector3 borderpoint;
    public float[] borderlist = new float[10];
    GameObject coordinate;
    GameObject borderLine;
    public void SetTransform(Transform a_transform)
    {
        Vector3 position = new Vector3(a_transform.position.x, a_transform.position.y + 0.1f, a_transform.position.z);
        count++;
        if(count == 1)
        {
            coordinate = Instantiate(origin,position, Quaternion.identity);
            coordinate.name = "origin";

        }
        else if (count == 2)
        {
            var border_name = "border";
            borderLine = Instantiate(border,position, Quaternion.identity);
            borderLine.name = border_name;


        }
        else
        {
            borderLine.transform.position = position;
        }
    }

    public Vector3 GetDistance()
    {

        float x = this.transform.position.x;
        float y = borderLine.transform.position.y - coordinate.transform.position.y;
        float z = borderLine.transform.position.z - coordinate.transform.position.z;

        point.x = x;
        point.y = y;
        point.z = z;
        return point;
    }*/

    public Vector3 GetMovePosition()
    {
        point = this.transform.localPosition;
        
        return point;
    }

}