using System.IO;
using UnityEngine;
using System;
using UnityEditor;
using Nifti.NET;
using System.Drawing.Printing;


namespace UnityVolumeRendering
{
    public class NiftiImporter
    {
        string filePath;
        string datasetName;

        public NiftiImporter(string filePath)
        {
            this.filePath = filePath;
        }

    private static double[,] GetAffineMatrix(string filePath)
     {
        var nifti = NiftiFile.Read(filePath);
        NiftiHeader header = nifti.Header;

        if (header.sform_code > 0)
        {
            // Usar SForm (más precisa normalmente)

            return new double[4, 4]
            {
                { header.srow_x[0], header.srow_x[1], header.srow_x[2], header.srow_x[3] },
                { header.srow_y[0], header.srow_y[1], header.srow_y[2], header.srow_y[3] },
                { header.srow_z[0], header.srow_z[1], header.srow_z[2], header.srow_z[3] },
                { 0, 0, 0, 1 }
            };
        }
        else if (header.qform_code > 0)
            {
                // Usar QForm si no hay SForm
                var affine = new double[4, 4];
                affine = ComputeAffineFromQuaternion(header.quatern_b,header.quatern_c,header.quatern_d,header.qoffset_x,header.qoffset_y,header.qoffset_z,header.pixdim);

                return affine;
        }
        else
        {
            // a rezar porque esté bien orientado
            throw new Exception("El archivo NIfTI no tiene información de orientación (sform/qform).");
        }
    }

        private static string GetOrientationFromAffine(double[,] affine)
        {
            // Se asume que affine es 4x4 y que las columnas 0,1,2 son los vectores de eje
            string[] axisLabelsPos = { "R", "A", "S" }; // X+, Y+, Z+
            string[] axisLabelsNeg = { "L", "P", "I" }; // X-, Y-, Z-

            string orientation = "";

            for (int col = 0; col < 3; col++)
            {
                // Extraer el eje como vector
                double x = affine[0, col];
                double y = affine[1, col];
                double z = affine[2, col];

                // Encontrar el componente dominante
                double absX = Math.Abs(x);
                double absY = Math.Abs(y);
                double absZ = Math.Abs(z);

                if (absX > absY && absX > absZ)
                {
                    orientation += (x > 0 ? axisLabelsPos[0] : axisLabelsNeg[0]);
                }
                else if (absY > absX && absY > absZ)
                {
                    orientation += (y > 0 ? axisLabelsPos[1] : axisLabelsNeg[1]);
                }
                else
                {
                    orientation += (z > 0 ? axisLabelsPos[2] : axisLabelsNeg[2]);
                }
            }

            return orientation;
        }
    
    private static double[,] ComputeAffineFromQuaternion(
        float qb, float qc, float qd,
        float qx, float qy, float qz,
        float[] pixdim)
    {
        float qfac = (pixdim[0] == 0) ? 1 : Math.Sign(pixdim[0]);
        float sx = pixdim[1];
        float sy = pixdim[2];
        float sz = pixdim[3];

        // Normalizar y calcular qa
        double a2 = 1.0 - (qb * qb + qc * qc + qd * qd);
        double qa = a2 > 0.0 ? Math.Sqrt(a2) : 0.0;

        // Rotación (matriz 3x3)
        double[,] R = new double[3, 3];
        R[0, 0] = qa * qa + qb * qb - qc * qc - qd * qd;
        R[0, 1] = 2.0 * (qb * qc - qa * qd);
        R[0, 2] = 2.0 * (qb * qd + qa * qc);
        R[1, 0] = 2.0 * (qb * qc + qa * qd);
        R[1, 1] = qa * qa + qc * qc - qb * qb - qd * qd;
        R[1, 2] = 2.0 * (qc * qd - qa * qb);
        R[2, 0] = 2.0 * (qb * qd - qa * qc);
        R[2, 1] = 2.0 * (qc * qd + qa * qb);
        R[2, 2] = qa * qa + qd * qd - qb * qb - qc * qc;

            // Aplicar escalas y qfac
            for (int i = 0; i < 3; i++)
            {
                R[i, 0] *= sx;
                R[i, 1] *= sy;
                R[i, 2] *= sz * qfac;
            }
        /*
        // Construir affine 4x4
        Matrix4x4 affine = new Matrix4x4();
        affine.m00 = (float)R[0, 0]; affine.m01 = (float)R[0, 1]; affine.m02 = (float)R[0, 2]; affine.m03 = (float)qx;
        affine.m10 = (float)R[1, 0]; affine.m11 = (float)R[1, 1]; affine.m12 = (float)R[1, 2]; affine.m13 = (float)qy;
        affine.m20 = (float)R[2, 0]; affine.m21 = (float)R[2, 1]; affine.m22 = (float)R[2, 2]; affine.m23 = (float)qz;
        affine.m30 = 0f; affine.m31 = 0f; affine.m32 = 0f; affine.m33 = 1f;
        */

        var affine = new double[4, 4]
            {
                { R[0, 0], R[0, 1], R[0, 2], qx },
                { R[1, 0], R[1, 1], R[1, 2], qy },
                { R[2, 0], R[2, 1], R[2, 2], qz },
                { 0, 0, 0, 1 }
            };
        return affine;
    }
   
    // HAY QUE CREAR UN VOLUME DATASET
    public VolumeDataset ImportNiftiDataset(string filePath)
        {
        var nifti = NiftiFile.Read(filePath);
        VolumeDataset dataset = new VolumeDataset();
        dataset.filePath = filePath;
        dataset.dimX = nifti.Header.dim[1];
        dataset.dimY = nifti.Header.dim[2];
        dataset.dimZ = nifti.Header.dim[3];
        int len_data = dataset.dimX * dataset.dimY * dataset.dimZ;
            //dataset.data = (float[])nifti.Data;
            var data_float = new float[len_data];
        for(int i = 0; i < len_data; i++)
            {
                data_float[i] = (float)nifti.Data[i];
            }
        


        //AQUÍ ES DONDE HAY QUE MIRAR ORIENTACIÓN Y SI ESO LA CARGAS AL REVÉS!!!
        double[,] affine= new double[4,4];
        affine=GetAffineMatrix(filePath);
        string orientation=GetOrientationFromAffine(affine);

            //hay que ver cómo nifti pasa de 3d a 1d oooo leer al revés sin más
            if (orientation[2] == 'I')
            {
                //tanteo máximo esto
                //pero esto son píxeles, hay que mirar las houndfield units????
                float[] data_inv = new float[len_data];
                for (int i = 0; i < len_data; i++)
                {
                    data_inv[i] = nifti.Data[len_data - i];
                }
                dataset.data = data_inv;
            }
            else
            {
                 dataset.data = data_float;

            }


        return dataset;
        }
    
    }
}

